using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Orleankka
{
    using Utility;

    class ActorPrototype
    {
        static readonly Dictionary<Type, ActorPrototype> cache =
                    new Dictionary<Type, ActorPrototype>();

        readonly Dictionary<Type, Func<object, object, Task<object>>> handlers = 
             new Dictionary<Type, Func<object, object, Task<object>>>();

        readonly HashSet<Type> reentrant;

        internal static void Register(Type actor)
        {
            var prototype = new ActorPrototype(actor);
            cache.Add(actor, prototype);
        }

        internal static void Reset()
        {
            cache.Clear();
        }

        internal static ActorPrototype Of(Type actor)
        {
            ActorPrototype prototype = cache.Find(actor);
            return prototype ?? new ActorPrototype(actor);
        }

        ActorPrototype(Type actor)
        {
            var attributes = actor.GetCustomAttributes<ReentrantAttribute>(inherit: true);
            reentrant = new HashSet<Type>(attributes.Select(x => x.Message));

            var methods = actor.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                               .Where(m =>
                                   m.IsPublic &&
                                   m.GetParameters().Length == 1 &&
                                   !m.GetParameters()[0].IsOut &&
                                   !m.GetParameters()[0].IsRetval &&
                                   !m.IsGenericMethod && !m.ContainsGenericParameters &&                                   
                                   (m.Name == "On" || m.Name == "Handle"));

            foreach (var method in methods)
                handlers.Add(method.GetParameters()[0].ParameterType, Bind.Handler(method, actor));
        }

        internal bool IsReentrant(Type message)
        {
            return reentrant.Contains(message);
        }

        internal Task<object> Dispatch(Actor target, object message)
        {
            var handler = handlers.Find(message.GetType());

            if (handler == null)
                throw new HandlerNotFoundException(message.GetType());

            return handler(target, message);
        }

        [Serializable]
        internal class HandlerNotFoundException : ApplicationException
        {
            const string description = "Can't find handler for '{0}'.\r\nCheck that handler method is public, has single arg and named 'On' or 'Handle'";

            internal HandlerNotFoundException(Type message)
                : base(string.Format(description, message))
            {}

            protected HandlerNotFoundException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {}
        }

        static class Bind
        {
            static readonly Task<object> Done = Task.FromResult((object)null);

            public static Func<object, object, Task<object>> Handler(MethodInfo method, Type actor)
            {
                if (typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    return method.ReturnType.GenericTypeArguments.Length != 0 
                            ? Lambda(typeof(Async<>), "Func", method.ReturnType.GenericTypeArguments[0], method, actor) 
                            : Lambda(typeof(Async<>), "Action", null, method, actor);
                }

                return method.ReturnType != typeof(void) 
                        ? Lambda(typeof(NonAsync<>), "Func", method.ReturnType, method, actor) 
                        : Lambda(typeof(NonAsync<>), "Action", null, method, actor);
            }

            static Func<object, object, Task<object>> Lambda(Type binder, string kind, Type arg, MethodInfo method, Type type)
            {
                var compiler = binder
                    .MakeGenericType(method.GetParameters()[0].ParameterType)
                    .GetMethod(kind, BindingFlags.Public | BindingFlags.Static);

                if (arg != null)
                    compiler = compiler.MakeGenericMethod(arg);
                
                return (Func<object, object, Task<object>>) compiler.Invoke(null, new object[] {method, type});
            }

            static class NonAsync<TRequest>
            {
                public static Func<object, object, Task<object>> Func<TResult>(MethodInfo method, Type type)
                {
                    return method.IsStatic ? StaticFunc<TResult>(method) : InstanceFunc<TResult>(method, type);
                }

                static Func<object, object, Task<object>> StaticFunc<TResult>(MethodInfo method)
                {
                    ParameterExpression request = Expression.Parameter(typeof(object));
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(null, method, new Expression[] { requestConversion });
                    var func = Expression.Lambda<Func<object, TResult>>(call, request).Compile();

                    return (t, r) => Task.FromResult((object)func(r));
                }

                static Func<object, object, Task<object>> InstanceFunc<TResult>(MethodInfo method, Type type)
                {
                    ParameterExpression target = Expression.Parameter(typeof(object));
                    ParameterExpression request = Expression.Parameter(typeof(object));

                    var targetConversion = Expression.Convert(target, type);
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(targetConversion, method, new Expression[] { requestConversion });
                    var func = Expression.Lambda<Func<object, object, TResult>>(call, target, request).Compile();

                    return (t, r) => Task.FromResult((object)func(t, r));
                }

                public static Func<object, object, Task<object>> Action(MethodInfo method, Type type)
                {
                    return method.IsStatic ? StaticAction(method) : InstanceAction(method, type);
                }

                static Func<object, object, Task<object>> StaticAction(MethodInfo method)
                {
                    ParameterExpression request = Expression.Parameter(typeof(object));
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(null, method, new Expression[] { requestConversion });
                    Action<object> action = Expression.Lambda<Action<object>>(call, request).Compile();

                    return (t, r) =>
                    {
                        action(r);
                        return Done;
                    };
                }

                static Func<object, object, Task<object>> InstanceAction(MethodInfo method, Type type)
                {
                    ParameterExpression target = Expression.Parameter(typeof(object));
                    ParameterExpression request = Expression.Parameter(typeof(object));

                    var targetConversion = Expression.Convert(target, type);
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(targetConversion, method, new Expression[] { requestConversion });
                    Action<object, object> action = Expression.Lambda<Action<object, object>>(call, target, request).Compile();

                    return (t, r) =>
                    {
                        action(t, r);
                        return Done;
                    };
                }
            }

            static class Async<TRequest>
            {
                public static Func<object, object, Task<object>> Func<TResult>(MethodInfo method, Type type)
                {
                    return method.IsStatic ? StaticFunc<TResult>(method) : InstanceFunc<TResult>(method, type);
                }

                static Func<object, object, Task<object>> StaticFunc<TResult>(MethodInfo method)
                {
                    ParameterExpression request = Expression.Parameter(typeof(object));
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(null, method, new Expression[] { requestConversion });
                    var func = Expression.Lambda<Func<object, Task<TResult>>>(call, request).Compile();

                    return async (t, r) => await func(r);
                }

                static Func<object, object, Task<object>> InstanceFunc<TResult>(MethodInfo method, Type type)
                {
                    ParameterExpression target = Expression.Parameter(typeof(object));
                    ParameterExpression request = Expression.Parameter(typeof(object));

                    var targetConversion = Expression.Convert(target, type);
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(targetConversion, method, new Expression[] { requestConversion });
                    var func = Expression.Lambda<Func<object, object, Task<TResult>>>(call, target, request).Compile();

                    return async (t, r) => await func(t, r);
                }

                public static Func<object, object, Task<object>> Action(MethodInfo method, Type type)
                {
                    return method.IsStatic ? StaticAction(method) : InstanceAction(method, type);
                }

                static Func<object, object, Task<object>> StaticAction(MethodInfo method)
                {
                    ParameterExpression request = Expression.Parameter(typeof(object));
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(null, method, new Expression[] { requestConversion });
                    Func<object, Task> func = Expression.Lambda<Func<object, Task>>(call, request).Compile();

                    return async (t, r) =>
                    {
                        await func(r);
                        return null;
                    };
                }

                static Func<object, object, Task<object>> InstanceAction(MethodInfo method, Type type)
                {
                    ParameterExpression target = Expression.Parameter(typeof(object));
                    ParameterExpression request = Expression.Parameter(typeof(object));

                    var targetConversion = Expression.Convert(target, type);
                    var requestConversion = Expression.Convert(request, typeof(TRequest));

                    var call = Expression.Call(targetConversion, method, new Expression[] { requestConversion });
                    Func<object, object, Task> func = Expression.Lambda<Func<object, object, Task>>(call, target, request).Compile();

                    return async (t, r) =>
                    {
                        await func(t, r);
                        return null;
                    };
                }
            }
        }
    }
}
﻿using System;
using System.Linq;

using Orleans;
using Orleans.Streams;

namespace Orleankka
{
    /// <summary>
    /// Serves as factory for acquiring actor references.
    /// </summary>
    public interface IActorSystem : IDisposable
    {
        /// <summary>
        /// Acquires the actor reference for the given actor path.
        /// </summary>
        /// <param name="path">The path of the actor</param>
        /// <returns>The actor reference</returns>
        ActorRef ActorOf(ActorPath path);

        /// <summary>
        /// Acquires the stream reference for the given stream path
        /// </summary>
        /// <param name="path">The path of the stream</param>
        /// <returns>The stream reference</returns>
        StreamRef StreamOf(StreamPath path);
    }

    /// <summary>
    /// Runtime implementation of <see cref="IActorSystem"/>
    /// </summary>
    public abstract class ActorSystem : MarshalByRefObject, IActorSystem
    {
        public static IActorSystemConfigurator Configure()
        {
            return null;
        }

        protected ActorSystem()
        {}

        public ActorRef ActorOf(ActorPath path)
        {
            if (path == ActorPath.Empty)
                throw new ArgumentException("Actor path is empty", "path");

           return ActorRef.Deserialize(path);
        }

        public StreamRef StreamOf(StreamPath path)
        {
            if (path == StreamPath.Empty)
                throw new ArgumentException("Stream path is empty", "path");

            return StreamRef.Deserialize(path);
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }

        public abstract void Dispose();
    }

    /// <summary>
    /// The actor system extensions.
    /// </summary>
    public static class ActorSystemExtensions
    {
        /// <summary>
        /// Acquires the actor reference for the given id and type of the actor.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor</typeparam>
        /// <param name="system">The reference to actor system</param>
        /// <param name="id">The id</param>
        /// <returns>An actor reference</returns>
        public static ActorRef ActorOf<TActor>(this IActorSystem system, string id) where TActor : Actor
        {
            return system.ActorOf(ActorPath.From(typeof(TActor), id));
        }
        
        /// <summary>
        /// Acquires the actor reference for the given actor path string.
        /// </summary>
        /// <param name="system">The reference to actor system</param>
        /// <param name="path">The path string</param>
        /// <returns>An actor reference</returns>
        public static ActorRef ActorOf(this IActorSystem system, string path)
        {
            return system.ActorOf(ActorPath.Parse(path));
        }

        /// <summary>
        /// Acquires the stream reference for the given id and type of the stream.
        /// </summary>
        /// <typeparam name="TStream">The type of the stream</typeparam>
        /// <param name="system">The reference to actor system</param>
        /// <param name="id">The id</param>
        /// <returns>A stream reference</returns>
        public static StreamRef StreamOf<TStream>(this IActorSystem system, string id) where TStream : IStreamProvider
        {
            return system.StreamOf(StreamPath.From(typeof(TStream), id));
        }
    }
}
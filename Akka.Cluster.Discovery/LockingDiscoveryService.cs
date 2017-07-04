﻿#region copyright
// -----------------------------------------------------------------------
// <copyright file="LockingDiscoveryService.cs" company="Akka.NET Project">
//    Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//    Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Discovery
{
    /// <summary>
    /// A specialization of a core <see cref="DiscoveryService"/>, making use of abilities
    /// to establish distributed locks within third-party services (if they provide such an
    /// option). Distributed locks are less risky and doesn't bring tendency to come up with
    /// races.
    /// 
    /// For situations where 3rd party cluster discovery service relies on provider that
    /// doesn't ensure any lock, please use <see cref="LocklessDiscoveryService"/>.
    /// </summary>
    public abstract class LockingDiscoveryService : DiscoveryService
    {
        private readonly LockingClusterDiscoverySettings settings;

        protected LockingDiscoveryService(LockingClusterDiscoverySettings settings) : base(settings)
        {
            this.settings = settings;
        }

        #region abstract members

        protected abstract Task<bool> LockAsync(string key);
        protected abstract Task UnlockAsync(string key);

        #endregion

        protected override void SendJoinSignal()
        {
            Self.Tell(Join.Instance);
        }

        protected override async Task<bool> TryJoinAsync()
        {
            var key = Context.System.Name;
            var locked = await LockAsync(key);
            if (locked)
            {
                try
                {
                    return await base.TryJoinAsync();
                }
                finally
                {
                    await UnlockAsync(key);
                }
            }
            else
            {
                Log.Warning("Failed to obtain a distributed lock for actor system [{0}]. Retry in [{1}]", key, settings.LockRetryInterval);
                Context.System.Scheduler.ScheduleTellOnce(settings.LockRetryInterval, Self, Join.Instance, ActorRefs.NoSender);
                return false;
            }
        }
    }

    public class LockingClusterDiscoverySettings : ClusterDiscoverySettings
    {
        /// <summary>
        /// In case if <see cref="LockingDiscoveryService"/> won't be able to acquire the lock,
        /// it will retry to do it again, max up to the number of times described by 
        /// <see cref="ClusterDiscoverySettings.JoinRetries"/> setting value.
        /// </summary>
        public TimeSpan LockRetryInterval { get; }

        public LockingClusterDiscoverySettings()
            : base()
        {
            LockRetryInterval = TimeSpan.FromMilliseconds(250);
        }

        public LockingClusterDiscoverySettings(TimeSpan aliveInterval, TimeSpan aliveTimeout, int joinRetries, TimeSpan lockRetryInterval) 
            : base(aliveInterval, aliveTimeout, joinRetries)
        {
            LockRetryInterval = lockRetryInterval;
        }

        public LockingClusterDiscoverySettings(Config config) : base(config)
        {
            LockRetryInterval = config.GetTimeSpan("lock-retry-interval", TimeSpan.FromMilliseconds(250));
        }
    }
}
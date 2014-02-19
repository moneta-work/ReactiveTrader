﻿using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Adaptive.ReactiveTrader.Client.Configuration;
using Adaptive.ReactiveTrader.Shared.Extensions;
using log4net;
using log4net.Config;

namespace Adaptive.ReactiveTrader.Client.Transport
{
    /// <summary>
    /// Connection provider provides always the same connection until it fails then create a new one a yield it
    /// Connection provider randomizes the list of server specified in configuration and then round robin through the list
    /// </summary>
    public class ConnectionProvider : IConnectionProvider
    {
        private readonly IUserProvider _userProvider;
        private readonly IObservable<IConnection> _connectionSequence;
        private readonly string[] _servers;

        private static readonly ILog Log = LogManager.GetLogger(typeof(ConnectionProvider));

        private int _currentIndex;

        public ConnectionProvider(IUserProvider userProvider, IConfigurationProvider configurationProvider)
        {
            _userProvider = userProvider;
            _servers = configurationProvider.Servers;
            _servers.Shuffle();

            _connectionSequence = CreateConnectionSequence();
        }

        public IObservable<IConnection> GetActiveConnection()
        {
            return _connectionSequence;
        }

        private IObservable<IConnection> CreateConnectionSequence()
        {
            return Observable.Create<IConnection>(o =>
            {
                Log.Info("Creating new connection...");
                var connection = GetNextConnection();

                var statusSubscription = connection.Status.Subscribe(
                    _ => { },
                    ex => o.OnCompleted(),
                    () =>
                    {
                        Log.Info("Status subscription completed");
                        o.OnCompleted();
                    });

                // TODO if we fail to connect we should not retry straight away to connect to same server, we need some back off
                var connectionSubscription =
                    connection.Initialize().Subscribe(
                        _ => o.OnNext(connection),
                        ex => o.OnCompleted(),
                        o.OnCompleted);

                return new CompositeDisposable { statusSubscription, connectionSubscription };
            })
                .Repeat()
                .Replay(1)
                .RefCount();
        }

        private IConnection GetNextConnection()
        {
            var connection = new Connection(_servers[_currentIndex++], _userProvider.Username);
            if (_currentIndex == _servers.Length)
            {
                _currentIndex = 0;
            }
            return connection;
        }
    }
}
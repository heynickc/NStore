﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NStore.Aggregates;
using NStore.InMemory;
using NStore.Persistence;
using NStore.Sample.Domain.Room;
using NStore.Sample.Projections;
using NStore.Sample.Support;
using NStore.SnapshotStore;
using NStore.Streams;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;

namespace NStore.Sample
{
    public class SampleApp : IDisposable
    {
        private readonly string _name;
        private int _rooms;

        private readonly IStreamStore _streams;
        private readonly IAggregateFactory _aggregateFactory;
        private readonly IReporter _reporter = new ColoredConsoleReporter("app", ConsoleColor.Gray);

        private readonly AppProjections _appProjections;
        private readonly ProfileDecorator _storeProfile;
        private readonly ProfileDecorator _snapshotProfile;
        private readonly ISnapshotStore _snapshots;
        readonly bool _quiet;
        private readonly PollingClient _poller;
        private readonly TaskProfilingInfo _cloneProfiler;

        public SampleApp(IPersistence store, string name, bool useSnapshots, bool quiet, bool fast)
        {
            _quiet = quiet;
            _name = name;
            _rooms = 32;
            _storeProfile = new ProfileDecorator(store);

            _streams = new StreamStore(_storeProfile);
            _aggregateFactory = new DefaultAggregateFactory();

            var network = fast
                ? (INetworkSimulator) new NoNetworkLatencySimulator()
                : (INetworkSimulator) new ReliableNetworkSimulator(10, 50);

            _appProjections = new AppProjections(network, quiet);

            _poller = new PollingClient(_storeProfile, _appProjections);

            if (useSnapshots)
            {
                _cloneProfiler = new TaskProfilingInfo("Cloning state");
                var inMemoryPersistence = new InMemoryPersistence(cloneFunc: CloneSnapshot);
                _snapshotProfile = new ProfileDecorator(inMemoryPersistence);
                _snapshots = new DefaultSnapshotStore(_snapshotProfile);
            }

            Subscribe();
        }

        private object CloneSnapshot(object arg)
        {
            if (arg == null)
                return null;

            return _cloneProfiler.Capture(() => ObjectSerializer.Clone(arg));
        }

        private IRepository GetRepository()
        {
            return new TplRepository(_aggregateFactory, _streams, _snapshots);
        }

        public async Task CreateRooms(int rooms)
        {
            _rooms = rooms;

            Stopwatch sw = new Stopwatch();
            sw.Start();
            int created = 0;

            async Task RoomBuilder(string id)
            {
                var repository = GetRepository(); // repository is not thread safe!
                var room = await repository.GetById<Room>(id);

                room.EnableBookings();

                await repository.Save(room, id + "_create").ConfigureAwait(false);

                Interlocked.Increment(ref created);

                if (!_quiet)
                {
                    _reporter.Report($"Listed Room {id}");
                }
            }

            var worker = new ActionBlock<string>(RoomBuilder, new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            });

            foreach (var i in Enumerable.Range(1, _rooms))
            {
                var id = GetRoomId(i);
                await worker.SendAsync(id).ConfigureAwait(false);
            }

            worker.Complete();
            await worker.Completion.ConfigureAwait(false);
            sw.Stop();

            if (created != _rooms)
            {
                throw new Exception("guru meditation!!!!");
            }

            _reporter.Report($"Listed {_rooms} rooms in {sw.ElapsedMilliseconds} ms");
        }

        private string GetRoomId(int room)
        {
            return $"Room_{room:D3}";
        }

        private void Subscribe()
        {
            _poller.Start();
        }

        public void Dispose()
        {
            _poller.Stop();
        }

        public void ShowRooms()
        {
            if (_quiet)
            {
                _reporter.Report($"Rooms projection: {_appProjections.Rooms.List.Count()} rooms listed");
            }
            else
            {
                _reporter.Report("Rooms:");
                foreach (var r in _appProjections.Rooms.List)
                {
                    _reporter.Report($"  room => {r.Id}");
                }
            }
        }

        public async Task AddSomeBookings(int bookings)
        {
            var rnd = new Random(DateTime.Now.Millisecond);
            long exceptions = 0;

            async Task BookRoom(int i)
            {
                var id = GetRoomId(rnd.Next(_rooms) + 1);

                var fromDate = DateTime.Today.AddDays(rnd.Next(10));
                var toDate = fromDate.AddDays(rnd.Next(5));

                while (true)
                {
                    try
                    {
                        var repository = GetRepository(); // repository is not thread safe!
                        var room = await repository.GetById<Room>(id);

                        room.AddBooking(new DateRange(fromDate, toDate));

                        await repository.Save(room, Guid.NewGuid().ToString()).ConfigureAwait(false);
                        return;
                    }
                    catch (DuplicateStreamIndexException)
                    {
                        Interlocked.Increment(ref exceptions);
                        if (!_quiet)
                        {
                            Console.WriteLine($"Concurrency exception on {id} => retry");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        return;
                    }
                }
            }

            var worker = new ActionBlock<int>(BookRoom, new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                BoundedCapacity = 2000
            });
            
            var sw = new Stopwatch();
            sw.Start();
            for (var i = 1; i <= bookings; i++)
            {
                await worker.SendAsync(i).ConfigureAwait(false);
            }
            worker.Complete();
            await worker.Completion.ConfigureAwait(false);
            
            sw.Stop();

            this._reporter.Report(
                $"Added {bookings} bookings (handling {exceptions} concurrency exceptions) in {sw.ElapsedMilliseconds} ms");
        }

        public void DumpMetrics()
        {
            this._reporter.Report(
                "* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *");
            this._reporter.Report(string.Empty);
            this._appProjections.DumpMetrics();
            this._reporter.Report(string.Empty);
            this._reporter.Report($"Persistence - {_name} provider");
            this._reporter.Report($"  {_storeProfile.PersistCounter}");
            this._reporter.Report($"  {_storeProfile.PartitionReadForwardCounter}");
            this._reporter.Report($"  {_storeProfile.PartitionReadBackwardCounter}");
            this._reporter.Report($"  {_storeProfile.PeekCounter}");
            this._reporter.Report($"  {_storeProfile.DeleteCounter}");
            this._reporter.Report($"  {_storeProfile.StoreScanCounter}");

            if (_snapshotProfile != null)
            {
                this._reporter.Report(string.Empty);
                this._reporter.Report($"Snapshots");
                this._reporter.Report($"  {_cloneProfiler}");
                this._reporter.Report($"  {_snapshotProfile.PersistCounter}");
                this._reporter.Report($"  {_snapshotProfile.PartitionReadForwardCounter}");
                this._reporter.Report($"  {_snapshotProfile.PartitionReadBackwardCounter}");
                this._reporter.Report($"  {_snapshotProfile.PeekCounter}");
                this._reporter.Report($"  {_snapshotProfile.DeleteCounter}");
                this._reporter.Report($"  {_snapshotProfile.StoreScanCounter}");
            }
            this._reporter.Report(string.Empty);
            this._reporter.Report(
                "* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *");
        }
    }
}
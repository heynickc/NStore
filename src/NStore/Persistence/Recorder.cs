﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NStore.Persistence
{
    public class Recorder : ISubscription
    {
        private sealed class Element
        {
            public Element(long index, object payload)
            {
                Index = index;
                Payload = payload;
            }

            public long Index { get; }
            public object Payload { get; }
        }

        private readonly IList<Element> _data = new List<Element>();
        private readonly IDictionary<long, object> _map = new Dictionary<long, object>();
        public IEnumerable<object> Data => _data.Select(x=>x?.Payload);
        public int Length => _data.Count;
        public bool ReadCompleted { get; private set; }

        public Task<bool> OnNext(IChunk data)
        {
            _data.Add(new Element(data.Index, data.Payload));
            _map[data.Index] = data.Payload;
            return Task.FromResult(true);
        }

        public Task Completed(long position)
        {
            ReadCompleted = true;
            return Task.CompletedTask;
        }

        public Task Stopped(long position)
        {
            ReadCompleted = true;
            return Task.CompletedTask;
        }

        public Task OnStart(long position)
        {
            return Task.CompletedTask;
        }

        public Task OnError(long position, Exception ex)
        {
            throw ex;
        }

        public void Replay(Action<object> action)
        {
            Replay(action, 0);
        }

        public void Replay(Action<object> action, int startAt)
        {
            for (var i = startAt; i < _data.Count; i++)
            {
                action(_data[i].Payload);
            }
        }

        public bool IsEmpty => _data.Count == 0;
        public object this[int position] => _data[position].Payload;

        public long GetIndex(int position) => _data[position].Index;
        public object ByIndex(int index) => _map[index];
    }
}
﻿namespace NStore.Domain
{
    public interface IEventSourcedAggregate
    {
        Changeset GetChangeSet();
        void ApplyChanges(Changeset changeset);

        void Loaded();
        void Persisted(Changeset changeset);
    }
}
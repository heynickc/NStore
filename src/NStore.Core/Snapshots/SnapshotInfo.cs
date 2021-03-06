﻿namespace NStore.Core.Snapshots
{
    public sealed class SnapshotInfo
    {
        public SnapshotInfo(
            string sourceId, 
            long sourceVersion, 
            object payload, 
            string schemaVersion)
        {
            SourceId = sourceId;
            SourceVersion = sourceVersion;
            Payload = payload;
            SchemaVersion = schemaVersion;
        }

        public long SourceVersion { get; private set; }
        public object Payload { get; private set; }
        public string SchemaVersion { get; private set; }
        public string SourceId { get; private set; }

        public bool IsEmpty => this.SourceId == null ||
                               this.SourceVersion == 0 ||
                               this.Payload == null ;
    }
}
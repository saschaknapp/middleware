﻿using System;

namespace fiskaltrust.Middleware.Storage.Azure.TableEntities.Configuration
{
    public class AzureFtQueueME : TableEntity
    {
        public Guid ftQueueMEId { get; set; }
        public Guid? ftSignaturCreationUnitMEId { get; set; }
        public string LastHash { get; set; } 
        public int SSCDFailCount { get; set; }
        public DateTime? SSCDFailMoment { get; set; }
        public Guid? SSCDFailQueueItemId { get; set; }
        public int UsedFailedCount { get; set; }
        public DateTime? UsedFailedMomentMin { get; set; }
        public DateTime? UsedFailedMomentMax { get; set; }
        public Guid? UsedFailedQueueItemId { get; set; }
        public long TimeStamp { get; set; }
        public int DailyClosingNumber { get; set; }
    }
}

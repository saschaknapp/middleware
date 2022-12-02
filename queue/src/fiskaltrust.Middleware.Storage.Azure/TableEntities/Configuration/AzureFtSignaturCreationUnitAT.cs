﻿using System;

namespace fiskaltrust.Middleware.Storage.Azure.TableEntities.Configuration
{
    public class AzureFtSignaturCreationUnitAT : BaseTableEntity
    {
        public Guid ftSignaturCreationUnitATId { get; set; }
        public string Url { get; set; }
        public string ZDA { get; set; }
        public string SN { get; set; }
        public string CertificateBase64 { get; set; }
        public int Mode { get; set; }
        public long TimeStamp { get; set; }
    }
}

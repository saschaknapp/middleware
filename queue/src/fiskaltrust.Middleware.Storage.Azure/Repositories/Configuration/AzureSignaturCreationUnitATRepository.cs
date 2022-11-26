﻿using System;
using Azure.Data.Tables;
using fiskaltrust.Middleware.Storage.Azure.Mapping;
using fiskaltrust.Middleware.Storage.Azure.TableEntities.Configuration;
using fiskaltrust.storage.V0;

namespace fiskaltrust.Middleware.Storage.Azure.Repositories.Configuration
{
    public class AzureSignaturCreationUnitATRepository : BaseAzureTableRepository<Guid, AzureFtSignaturCreationUnitAT, ftSignaturCreationUnitAT>
    {
        public AzureSignaturCreationUnitATRepository(QueueConfiguration queueConfig, TableServiceClient tableServiceClient)
            : base(queueConfig, tableServiceClient, nameof(ftSignaturCreationUnitAT)) { }

        protected override void EntityUpdated(ftSignaturCreationUnitAT entity) => entity.TimeStamp = DateTime.UtcNow.Ticks;

        protected override Guid GetIdForEntity(ftSignaturCreationUnitAT entity) => entity.ftSignaturCreationUnitATId;

        protected override AzureFtSignaturCreationUnitAT MapToAzureEntity(ftSignaturCreationUnitAT entity) => Mapper.Map(entity);

        protected override ftSignaturCreationUnitAT MapToStorageEntity(AzureFtSignaturCreationUnitAT entity) => Mapper.Map(entity);
    }
}

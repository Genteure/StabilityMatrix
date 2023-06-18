﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using LiteDB;
using LiteDB.Async;
using StabilityMatrix.Extensions;
using StabilityMatrix.Models.Api;

namespace StabilityMatrix.Database;

public class LiteDbContext : ILiteDbContext
{
    public LiteDatabaseAsync Database { get; }

    // Notification events
    public event EventHandler? CivitModelsChanged;
    
    // Collections (Tables)
    public ILiteCollectionAsync<CivitModel> CivitModels => Database.GetCollection<CivitModel>("CivitModels");
    public ILiteCollectionAsync<CivitModelVersion> CivitModelVersions => Database.GetCollection<CivitModelVersion>("CivitModelVersions");
    public ILiteCollectionAsync<CivitModelQueryCacheEntry> CivitModelQueryCache => Database.GetCollection<CivitModelQueryCacheEntry>("CivitModelQueryCache");

    public LiteDbContext(string connectionString)
    {
        Database = new LiteDatabaseAsync(connectionString);

        // Register reference fields
        LiteDBExtensions.Register<CivitModel, CivitModelVersion>(m => m.ModelVersions, "CivitModelVersions");
        LiteDBExtensions.Register<CivitModelQueryCacheEntry, CivitModel>(e => e.Items, "CivitModels");
    }
    
    public async Task<(CivitModel?, CivitModelVersion?)> FindCivitModelFromFileHashAsync(string hashBlake3)
    {
        var version = await CivitModelVersions.Query()
            .Where(mv => mv.Files!
                .Select(f => f.Hashes)
                .Select(hashes => hashes.BLAKE3)
                .Any(hash => hash == hashBlake3))
            .FirstOrDefaultAsync();
        if (version is null) return (null, null);
        var model = await CivitModels.Query()
            .Include(m => m.ModelVersions)
            .Where(m => m.ModelVersions!
                .Select(v => v.Id)
                .Any(id => id == version.Id))
            .FirstOrDefaultAsync();
        return (model, version);
    }
    
    public async Task<bool> UpsertCivitModelAsync(CivitModel civitModel)
    {
        // Insert model versions first then model
        var versionsUpdated = await CivitModelVersions.UpsertAsync(civitModel.ModelVersions);
        var updated = await CivitModels.UpsertAsync(civitModel);
        // Notify listeners on any change
        var anyUpdated = versionsUpdated > 0 || updated;
        if (anyUpdated)
        {
            CivitModelsChanged?.Invoke(this, EventArgs.Empty);
        }
        return anyUpdated;
    }
    
    public async Task<bool> UpsertCivitModelAsync(IEnumerable<CivitModel> civitModels)
    {
        var civitModelsArray = civitModels.ToArray();
        // Get all model versions then insert models
        var versions = civitModelsArray.SelectMany(model => model.ModelVersions ?? new());
        var versionsUpdated = await CivitModelVersions.UpsertAsync(versions);
        var updated = await CivitModels.UpsertAsync(civitModelsArray);
        // Notify listeners on any change
        var anyUpdated = versionsUpdated > 0 || updated > 0;
        if (updated > 0 || versionsUpdated > 0)
        {
            CivitModelsChanged?.Invoke(this, EventArgs.Empty);
        }
        return anyUpdated;
    }
    
    // Add to cache
    public async Task<bool> UpsertCivitModelQueryCacheEntryAsync(CivitModelQueryCacheEntry entry)
    {
        var changed = await CivitModelQueryCache.UpsertAsync(entry);
        if (changed)
        {
            CivitModelsChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public void Dispose()
    {
        Database.Dispose();
        GC.SuppressFinalize(this);
    }
}

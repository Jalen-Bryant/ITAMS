using System.Globalization;
using ITAMS.Api.Configuration;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Services;

public sealed class ReportsService
{
    public const string Last30DaysPreset = "30d";
    public const string Last90DaysPreset = "90d";
    public const string Last12MonthsPreset = "12m";
    public const string CustomPreset = "custom";
    public const string DailyGranularity = "day";
    public const string MonthlyGranularity = "month";

    public static readonly string[] SupportedPresets =
    [
        Last30DaysPreset,
        Last90DaysPreset,
        Last12MonthsPreset,
        CustomPreset
    ];

    private readonly IMongoCollection<AssetDocument> _assetsCollection;
    private readonly IMongoCollection<AssignmentDocument> _assignmentsCollection;
    private readonly IMongoCollection<LifecycleEventDocument> _lifecycleEventsCollection;
    private readonly IMongoCollection<UserDocument> _usersCollection;

    public ReportsService(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
    {
        var mongoDbSettings = settings.Value;
        var database = mongoClient.GetDatabase(mongoDbSettings.DatabaseName);

        _assetsCollection = database.GetCollection<AssetDocument>(mongoDbSettings.AssetsCollectionName);
        _assignmentsCollection = database.GetCollection<AssignmentDocument>(mongoDbSettings.AssignmentsCollectionName);
        _lifecycleEventsCollection = database.GetCollection<LifecycleEventDocument>(mongoDbSettings.LifecycleEventsCollectionName);
        _usersCollection = database.GetCollection<UserDocument>(mongoDbSettings.UsersCollectionName);
    }

    public async Task<ReportsOverviewResponse> GetOverviewAsync(
        ReportsOverviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolvedRange = ResolveRange(request);
        var assetDepartment = NormalizeOptionalValue(request.AssetDepartment);
        var userDepartment = NormalizeOptionalValue(request.UserDepartment);

        var assetDepartmentsTask = GetDepartmentOptionsAsync(_assetsCollection, cancellationToken);
        var userDepartmentsTask = GetDepartmentOptionsAsync(_usersCollection, cancellationToken);
        var assetsTask = _assetsCollection
            .Find(BuildAssetDepartmentFilter(assetDepartment))
            .ToListAsync(cancellationToken);
        var usersTask = _usersCollection
            .Find(BuildUserDepartmentFilter(userDepartment))
            .ToListAsync(cancellationToken);

        await Task.WhenAll(assetDepartmentsTask, userDepartmentsTask, assetsTask, usersTask);

        var assets = assetsTask.Result;
        var users = usersTask.Result;
        var assetIds = assets.Select(asset => asset.Id).ToHashSet();

        var assignmentsTask = _assignmentsCollection
            .Find(BuildAssignmentDateFilter(resolvedRange))
            .ToListAsync(cancellationToken);
        var lifecycleEventsTask = _lifecycleEventsCollection
            .Find(BuildLifecycleDateFilter(resolvedRange))
            .ToListAsync(cancellationToken);

        await Task.WhenAll(assignmentsTask, lifecycleEventsTask);

        var assignments = assignmentsTask.Result
            .Where(assignment => assetDepartment is null || assetIds.Contains(assignment.AssetId))
            .ToList();
        var lifecycleEvents = lifecycleEventsTask.Result
            .Where(lifecycleEvent => assetDepartment is null || assetIds.Contains(lifecycleEvent.AssetId))
            .ToList();

        return new ReportsOverviewResponse
        {
            Filters = new ReportsFilterStateResponse
            {
                Preset = resolvedRange.Preset,
                StartDate = resolvedRange.StartDateUtc,
                EndDate = resolvedRange.EndDateUtc,
                AssetDepartment = assetDepartment,
                UserDepartment = userDepartment,
                TimeGranularity = resolvedRange.TimeGranularity
            },
            AvailableFilters = new ReportsFilterOptionsResponse
            {
                AssetDepartments = assetDepartmentsTask.Result,
                UserDepartments = userDepartmentsTask.Result
            },
            Kpis = new ReportsKpiResponse
            {
                TotalAssets = assets.Count,
                OpenAssignments = assets.Count(asset => asset.CurrentAssignment is not null),
                TotalUsers = users.Count,
                WarrantiesExpiringSoon = assets.Count(asset =>
                {
                    var warrantyEndDate = EnsureUtc(asset.WarrantyEndDate);
                    return warrantyEndDate >= resolvedRange.StartDateUtc &&
                           warrantyEndDate < resolvedRange.EndDateExclusiveUtc;
                })
            },
            AssetsByStatus = BuildBreakdown(assets, asset => asset.Status),
            AssetsByType = BuildBreakdown(assets, asset => asset.Type),
            UsersByRole = BuildBreakdown(users, user => user.Role),
            UsersByDepartment = BuildBreakdown(users, user => user.Department),
            AssignmentsOverTime = BuildTimeSeries(assignments.Select(assignment => assignment.StartDate), resolvedRange),
            WarrantyExpirationsOverTime = BuildTimeSeries(
                assets.Select(asset => asset.WarrantyEndDate)
                    .Where(date =>
                    {
                        var utcDate = EnsureUtc(date);
                        return utcDate >= resolvedRange.StartDateUtc &&
                               utcDate < resolvedRange.EndDateExclusiveUtc;
                    }),
                resolvedRange),
            LifecycleActivityOverTime = BuildTimeSeries(lifecycleEvents.Select(item => item.Timestamp), resolvedRange)
        };
    }

    private static FilterDefinition<AssetDocument> BuildAssetDepartmentFilter(string? assetDepartment) =>
        string.IsNullOrWhiteSpace(assetDepartment)
            ? FilterDefinition<AssetDocument>.Empty
            : Builders<AssetDocument>.Filter.Eq(asset => asset.Department, assetDepartment);

    private static FilterDefinition<UserDocument> BuildUserDepartmentFilter(string? userDepartment) =>
        string.IsNullOrWhiteSpace(userDepartment)
            ? FilterDefinition<UserDocument>.Empty
            : Builders<UserDocument>.Filter.Eq(user => user.Department, userDepartment);

    private static FilterDefinition<AssignmentDocument> BuildAssignmentDateFilter(ResolvedReportsRange range) =>
        Builders<AssignmentDocument>.Filter.And(
            Builders<AssignmentDocument>.Filter.Gte(assignment => assignment.StartDate, range.StartDateUtc),
            Builders<AssignmentDocument>.Filter.Lt(assignment => assignment.StartDate, range.EndDateExclusiveUtc));

    private static FilterDefinition<LifecycleEventDocument> BuildLifecycleDateFilter(ResolvedReportsRange range) =>
        Builders<LifecycleEventDocument>.Filter.And(
            Builders<LifecycleEventDocument>.Filter.Gte(item => item.Timestamp, range.StartDateUtc),
            Builders<LifecycleEventDocument>.Filter.Lt(item => item.Timestamp, range.EndDateExclusiveUtc));

    private static List<ReportsBreakdownItemResponse> BuildBreakdown<T>(
        IEnumerable<T> values,
        Func<T, string> selector) =>
        values
            .GroupBy(value => NormalizeBreakdownLabel(selector(value)), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportsBreakdownItemResponse
            {
                Label = group.Key,
                Value = group.Count()
            })
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<ReportsTimeSeriesPointResponse> BuildTimeSeries(
        IEnumerable<DateTime> timestamps,
        ResolvedReportsRange range)
    {
        var groupedValues = timestamps
            .Select(EnsureUtc)
            .Where(timestamp => timestamp >= range.StartDateUtc && timestamp < range.EndDateExclusiveUtc)
            .GroupBy(timestamp => GetBucketStart(timestamp, range.TimeGranularity))
            .ToDictionary(group => group.Key, group => group.Count());

        return GetBuckets(range)
            .Select(bucketStart => new ReportsTimeSeriesPointResponse
            {
                BucketStart = bucketStart,
                Label = FormatBucketLabel(bucketStart, range.TimeGranularity),
                Value = groupedValues.GetValueOrDefault(bucketStart, 0)
            })
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetDepartmentOptionsAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        CancellationToken cancellationToken)
    {
        var cursor = await collection.DistinctAsync<string>(
            "department",
            Builders<TDocument>.Filter.Ne("department", string.Empty),
            cancellationToken: cancellationToken);

        var values = await cursor.ToListAsync(cancellationToken);

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<DateTime> GetBuckets(ResolvedReportsRange range)
    {
        if (string.Equals(range.TimeGranularity, DailyGranularity, StringComparison.Ordinal))
        {
            for (var bucketStart = range.StartDateUtc; bucketStart < range.EndDateExclusiveUtc; bucketStart = bucketStart.AddDays(1))
            {
                yield return bucketStart;
            }

            yield break;
        }

        var current = GetMonthStart(range.StartDateUtc);
        var last = GetMonthStart(range.EndDateUtc);
        while (current <= last)
        {
            yield return current;
            current = current.AddMonths(1);
        }
    }

    private static DateTime GetBucketStart(DateTime value, string timeGranularity) =>
        string.Equals(timeGranularity, DailyGranularity, StringComparison.Ordinal)
            ? value.Date
            : GetMonthStart(value);

    private static string FormatBucketLabel(DateTime bucketStart, string timeGranularity) =>
        string.Equals(timeGranularity, DailyGranularity, StringComparison.Ordinal)
            ? bucketStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : bucketStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static ResolvedReportsRange ResolveRange(ReportsOverviewRequest request)
    {
        var preset = NormalizeOptionalValue(request.Preset) ?? Last12MonthsPreset;
        var todayUtc = DateTime.UtcNow.Date;

        DateTime startDateUtc;
        DateTime endDateUtc;

        switch (preset)
        {
            case Last30DaysPreset:
                endDateUtc = todayUtc;
                startDateUtc = todayUtc.AddDays(-29);
                break;
            case Last90DaysPreset:
                endDateUtc = todayUtc;
                startDateUtc = todayUtc.AddDays(-89);
                break;
            case CustomPreset:
                startDateUtc = ToUtcDate(ParseDateOnly(request.StartDate));
                endDateUtc = ToUtcDate(ParseDateOnly(request.EndDate));
                break;
            default:
                endDateUtc = todayUtc;
                startDateUtc = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);
                preset = Last12MonthsPreset;
                break;
        }

        return new ResolvedReportsRange(
            preset,
            startDateUtc,
            endDateUtc,
            endDateUtc.AddDays(1),
            GetTimeGranularity(preset, startDateUtc, endDateUtc));
    }

    private static string GetTimeGranularity(string preset, DateTime startDateUtc, DateTime endDateUtc)
    {
        if (string.Equals(preset, Last12MonthsPreset, StringComparison.Ordinal))
        {
            return MonthlyGranularity;
        }

        var daySpan = (endDateUtc - startDateUtc).TotalDays;
        return daySpan > 90 ? MonthlyGranularity : DailyGranularity;
    }

    private static DateOnly ParseDateOnly(string? value) =>
        DateOnly.ParseExact(value!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeBreakdownLabel(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Unspecified" : value.Trim();

    private static DateTime ToUtcDate(DateOnly value) =>
        DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    private static DateTime GetMonthStart(DateTime value) =>
        new(value.Year, value.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private sealed record ResolvedReportsRange(
        string Preset,
        DateTime StartDateUtc,
        DateTime EndDateUtc,
        DateTime EndDateExclusiveUtc,
        string TimeGranularity);
}

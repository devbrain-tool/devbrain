namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Models;

public interface IGrowthStore
{
    Task<DeveloperMetric> AddMetric(DeveloperMetric metric);
    Task<IReadOnlyList<DeveloperMetric>> GetMetrics(string dimension, int weeks = 12);
    Task<IReadOnlyList<DeveloperMetric>> GetLatestMetrics();

    Task<GrowthMilestone> AddMilestone(GrowthMilestone milestone);
    Task<IReadOnlyList<GrowthMilestone>> GetMilestones(int limit = 50);

    Task<GrowthReport> AddReport(GrowthReport report);
    Task<GrowthReport?> GetLatestReport();
    Task<IReadOnlyList<GrowthReport>> GetReports(int limit = 12);

    Task Clear();
}

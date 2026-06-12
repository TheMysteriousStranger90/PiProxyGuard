namespace PiProxyGuard.Api.Contracts;

public record TrafficSummaryDto(
    DateTime FromUtc,
    DateTime ToUtc,
    long TotalRequests,
    long TotalBytes,
    long DeniedRequests,
    int UniqueClients,
    int UniqueHosts);

public record TopHostDto(string Host, long Requests, long Bytes);

public record TopClientDto(string ClientIp, long Requests, long Bytes, long DeniedRequests);

public record TimelinePointDto(DateTime BucketStartUtc, long Requests, long Bytes);

public record StatusCodeDto(int StatusCode, long Count);

public record AddBlockedDomainRequest(string Domain, string? Reason);

public record BlockedDomainDto(long Id, string Domain, string Source, string? Reason, DateTime CreatedAtUtc, bool IsActive);

public record AlertDto(
    long Id,
    string ClientIp,
    string Type,
    string Description,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    DateTime DetectedAtUtc,
    bool IsAcknowledged);

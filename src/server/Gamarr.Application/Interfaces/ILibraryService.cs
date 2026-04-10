using Gamarr.Application.Contracts;
using Gamarr.Domain.Enums;

namespace Gamarr.Application.Interfaces;

public interface ILibraryService
{
    Task<IReadOnlyCollection<LibraryTitleResponse>> ListAsync(Guid? machineId, string? genre, string? studio, int? year, string? sortBy, CancellationToken cancellationToken);
    Task<LibraryTitleDetailResponse?> GetAsync(Guid packageId, Guid? machineId, CancellationToken cancellationToken);
    Task<LibraryReconcilePreviewResponse> PreviewReconcileAsync(Guid packageId, CancellationToken cancellationToken);
    Task<LibraryTitleDetailResponse> ApplyReconcileAsync(Guid packageId, ApplyLibraryReconcileRequest request, Guid? machineId, CancellationToken cancellationToken);
    Task<ManualMetadataSearchResponse> SearchPackageMetadataAsync(Guid packageId, ManualMetadataSearchRequest request, CancellationToken cancellationToken);
    Task<LibraryTitleDetailResponse> ApplyPackageMetadataSearchAsync(Guid packageId, ApplyManualMetadataMatchRequest request, Guid? machineId, CancellationToken cancellationToken);
    Task<LibraryTitleDetailResponse> ArchiveAsync(Guid packageId, string? reason, Guid? machineId, CancellationToken cancellationToken);
    Task<LibraryTitleDetailResponse> RestoreAsync(Guid packageId, Guid? machineId, CancellationToken cancellationToken);
    Task<PlayLibraryTitleResponse> PlayAsync(Guid packageId, PlayLibraryTitleRequest request, CancellationToken cancellationToken);
    Task<PlayLibraryTitleResponse> ValidateInstallAsync(Guid packageId, LibraryMachineActionRequest request, CancellationToken cancellationToken);
    Task<PlayLibraryTitleResponse> UninstallAsync(Guid packageId, LibraryMachineActionRequest request, CancellationToken cancellationToken);
    Task<LibraryTitleDetailResponse> MarkNotInstalledAsync(Guid packageId, LibraryMachineActionRequest request, CancellationToken cancellationToken);
    Task<ResetLibraryResponse> ResetAsync(ResetLibraryRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LibraryRootResponse>> ListRootsAsync(CancellationToken cancellationToken);
    Task<LibraryRootResponse> CreateRootAsync(CreateLibraryRootRequest request, CancellationToken cancellationToken);
    Task<LibraryScanResponse> ScanRootAsync(Guid rootId, CancellationToken cancellationToken);
    Task ExecuteScanAsync(Guid scanId, CancellationToken cancellationToken);
    Task<LibraryScanResponse?> GetScanAsync(Guid scanId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LibraryScanResponse>> ListScansAsync(Guid? rootId, LibraryScanState? state, CancellationToken cancellationToken);
    Task<LibraryScanResponse> CancelScanAsync(Guid scanId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<LibraryCandidateResponse>> ListCandidatesAsync(LibraryCandidateStatus? status, Guid? rootId, Guid? scanId, string? search, CancellationToken cancellationToken);
    Task<LibraryCandidateResponse> ApproveCandidateAsync(Guid candidateId, CancellationToken cancellationToken);
    Task<LibraryCandidateResponse> RejectCandidateAsync(Guid candidateId, CancellationToken cancellationToken);
    Task<LibraryCandidateResponse> MergeCandidateAsync(Guid candidateId, MergeLibraryCandidateRequest request, CancellationToken cancellationToken);
    Task<LibraryCandidateResponse> UnmergeCandidateAsync(Guid candidateId, CancellationToken cancellationToken);
    Task<LibraryCandidateResponse> ReplaceMergeTargetAsync(Guid candidateId, ReplaceMergeTargetRequest request, CancellationToken cancellationToken);
    Task<LibraryCandidateResponse> SelectCandidateMatchAsync(Guid candidateId, SelectLibraryCandidateMatchRequest request, CancellationToken cancellationToken);
    Task<ManualMetadataSearchResponse> SearchCandidateMetadataAsync(Guid candidateId, ManualMetadataSearchRequest request, CancellationToken cancellationToken);
    Task<LibraryCandidateResponse> ApplyCandidateMetadataSearchAsync(Guid candidateId, ApplyManualMetadataMatchRequest request, CancellationToken cancellationToken);
    Task<BulkReMatchResponse> BulkReMatchAsync(CancellationToken cancellationToken);
}

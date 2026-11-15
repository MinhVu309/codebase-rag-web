using Application.Abstractions;
using Application.Common;
using Application.Rag;
using Domain.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Repositories;

/// <summary>
/// Code viewer proxy (W09). Order matters: ownership (404, indistinguishable from "does not exist") -> input
/// validation (400) -> repository state (409, RAG not called) -> RAG. The RAG <c>index_id</c> never leaves this class,
/// and neither the requested path nor the returned content is logged (user/repository content).
/// </summary>
public class CodeFileService(IAppDbContext db, IRagClient rag, IValidator<ReadFileRequest> validator)
{
    public async Task<CodeFileDto> ReadAsync(Guid userId, Guid repositoryId, ReadFileRequest request, CancellationToken ct)
    {
        var repo = await db.Repositories.AsNoTracking()
                       .FirstOrDefaultAsync(r => r.Id == repositoryId && r.Project.UserId == userId, ct)
                   ?? throw new NotFoundException("Repository not found.");

        await validator.EnsureValidAsync(request, ct);
        var (start, end) = FileRange.Resolve(request.Start, request.End);

        if (repo.Status == IndexStatus.Failed)
            throw new ConflictException("Repository đã index thất bại. Hãy xoá và import lại.", "REPOSITORY_FAILED");
        if (repo.IndexId is null)
            throw new ConflictException("Repository chưa có chỉ mục để đọc file.", "NO_REPOSITORY");
        if (repo.Status != IndexStatus.Ready)
            throw new RagException(RagFailureKind.NotReady, RagErrorCodes.IndexNotReady);

        var file = await rag.GetFileAsync(repo.IndexId, request.Path!, start, end, ct);
        return new CodeFileDto(file.Path, file.Start, file.End, file.Content, file.Language, repo.CommitSha);
    }
}

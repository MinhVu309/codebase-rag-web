using Application.Rag;
using Microsoft.AspNetCore.Mvc;

namespace Api.Middleware;

/// <summary>
/// Turns a classified RAG failure into the ProblemDetails the frontend sees (plan section 4). The stable
/// machine-readable <c>code</c> extension lets the frontend branch (e.g. FILE_NOT_FOUND -> show the citation
/// snapshot). Upstream messages and the fact that an API key was rejected are never exposed.
/// </summary>
public static class RagProblemMapper
{
    private const string Unavailable = "Hệ thống AI tạm thời không khả dụng. Vui lòng thử lại sau.";

    public static ProblemDetails ToProblem(RagException ex)
    {
        var (status, title, detail, code) = ex.Kind switch
        {
            RagFailureKind.NotReady => (409, "Repository chưa sẵn sàng",
                "Repository chưa được index xong. Vui lòng chờ trạng thái 'ready' rồi thử lại.", RagErrorCodes.IndexNotReady),

            RagFailureKind.NotFound when ex.Code == RagErrorCodes.FileNotFound => (404, "Không tìm thấy file",
                "File không còn tồn tại trong phiên bản đã index.", RagErrorCodes.FileNotFound),
            RagFailureKind.NotFound => (404, "Không tìm thấy chỉ mục",
                "Chỉ mục của repository không còn tồn tại.", RagErrorCodes.IndexNotFound),

            RagFailureKind.Invalid when ex.Code == RagErrorCodes.InvalidRange => (400, "Khoảng dòng không hợp lệ",
                "Khoảng dòng yêu cầu không hợp lệ (tối đa 2000 dòng mỗi lần).", RagErrorCodes.InvalidRange),
            RagFailureKind.Invalid when ex.Code == RagErrorCodes.RepoTooLarge => (422, "Repository quá lớn",
                "Repository vượt quá giới hạn kích thước được hỗ trợ.", RagErrorCodes.RepoTooLarge),
            RagFailureKind.Invalid => (422, "Repository không hợp lệ",
                "URL repository không hợp lệ hoặc không được hỗ trợ.", ex.Code),

            RagFailureKind.LlmError => (502, "Hệ thống AI gặp lỗi",
                "Hệ thống AI tạm thời gặp lỗi. Vui lòng thử lại.", RagErrorCodes.LlmError),
            RagFailureKind.Timeout => (504, "Hệ thống AI quá hạn",
                "Hệ thống AI phản hồi quá hạn. Vui lòng thử lại.", RagErrorCodes.Timeout),

            // Unauthorized = our key was rejected: a deployment problem, not something to tell the user.
            _ => (502, "Hệ thống AI không khả dụng", Unavailable, RagErrorCodes.Unavailable),
        };

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return problem;
    }
}

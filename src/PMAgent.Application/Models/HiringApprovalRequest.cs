namespace PMAgent.Application.Models;

public sealed record HiringApprovalRequest(
    bool Approved = true,
    string Comment = "");
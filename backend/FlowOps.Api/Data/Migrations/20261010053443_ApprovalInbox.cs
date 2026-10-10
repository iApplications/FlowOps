using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOps.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssigneeRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RequesterUserId = table.Column<string>(type: "text", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_WorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "WorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalTaskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTaskEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalTaskEvents_ApprovalTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "ApprovalTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalTaskEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTaskEvents_OrganizationId",
                table: "ApprovalTaskEvents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTaskEvents_TaskId_CreatedAt",
                table: "ApprovalTaskEvents",
                columns: new[] { "TaskId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_OrganizationId_Status_AssigneeRole",
                table: "ApprovalTasks",
                columns: new[] { "OrganizationId", "Status", "AssigneeRole" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_WorkflowInstanceId",
                table: "ApprovalTasks",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_WorkflowInstanceId_NodeId",
                table: "ApprovalTasks",
                columns: new[] { "WorkflowInstanceId", "NodeId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'ChangesRequested')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalTaskEvents");

            migrationBuilder.DropTable(
                name: "ApprovalTasks");
        }
    }
}

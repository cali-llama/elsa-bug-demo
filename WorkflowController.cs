using Elsa.Alterations.AlterationTypes;
using Elsa.Alterations.Core.Contracts;
using Elsa.Common.Entities;
using Elsa.Workflows;
using Elsa.Extensions;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.OrderDefinitions;
using Microsoft.AspNetCore.Mvc;

namespace ElsaServerBugDemo;

[ApiController]
[Route("workflow")]
public class WorkflowController(IWorkflowBuilderFactory workflowBuilderFactory, IWorkflowRunner workflowRunner,
    IWorkflowExecutionLogStore workflowExecutionLogStore, IAlterationRunner alterationRunner, 
    IAlteredWorkflowDispatcher alteredWorkflowDispatcher, IWorkflowInstanceStore workflowInstanceStore,
    IBookmarkResumer bookmarkResumer, IBookmarkStore bookmarkStore) : ControllerBase
{
    private const string DefinitionId = nameof(FaultingBookmarkWorkflow);
    private const string DefVersionId = "FaultingBookmarkWorkflow:v1";
    
    private Workflow? _workflow;

    private async Task<Workflow> LoadWorkflow()
    {
        if (_workflow != null) return _workflow;
        _workflow = await workflowBuilderFactory.CreateBuilder().BuildWorkflowAsync<FaultingBookmarkWorkflow>();
        _workflow.Identity = new WorkflowIdentity(DefinitionId, 1, DefVersionId);
        return _workflow;
    }
    
    [HttpPost]
    [Route("start")]
    public async Task<IResult> StartWorkflow()
    {
        var workflow = await LoadWorkflow();
        var result = await workflowRunner.RunAsync(workflow);

        if (result.WorkflowState.Incidents.Count == 0)
        {
            // as long as initial state for env variable `FaultWorkflow` has value `1`,
            // this block should never be hit, but was useful in initial setup testing 
            var bookmark = result.WorkflowState.Bookmarks.FirstOrDefault(x => x.ActivityId == "Resume");

            // Resume workflow.
            var runOptions = new RunWorkflowOptions { BookmarkId = bookmark!.Id };
            var resumeResult = await workflowRunner.RunAsync(workflow, result.WorkflowState, runOptions);
        
            return Results.Ok($"Workflow Status: {resumeResult.WorkflowState.Status}, SubStatus: {resumeResult.WorkflowState.SubStatus}");
        }
        
        // we have an error, so first set the env variable so next run of `FaultedEvent` will not raise exception
        Environment.SetEnvironmentVariable(Constants.FaultWorkflowEnvVar, "0");

        // now run alteration to schedule activity from faulted activity
        var instanceId = await GetDatabaseInstanceId(DefinitionId);
        var faultedActivityId = result.WorkflowState.Incidents.First().ActivityId;
        var faultedActivityInstanceId = await GetFaultedActivityInstanceIdAsync(instanceId, faultedActivityId);
        var alterations = new List<IAlteration>
        {
            // it's also possible to pass the `ActivityId`, but in Elsa v3.1.1 this caused ActivityExecutionContext hierarchy to be incorrect, post alteration.
            // CreateAndAssignTask1 parent was pointing to Flowchart (grandparent) instead of Sequence (parent)
            new ScheduleActivity { ActivityInstanceId = faultedActivityInstanceId }
        };
        var altResult = await alterationRunner.RunAsync(instanceId, alterations);
        if (altResult is null) throw new Exception($"No alteration result found from AlterationRunner!");
            
        // now we have to resume the workflow manually to resume from faulted activity
        await alteredWorkflowDispatcher.DispatchAsync(altResult);

        var msg = altResult.IsSuccessful ? "Successful" : "FAILED";
        return Results.Ok($"Workflow Instance ID: {instanceId} alteration was: {msg}");
    }

    [HttpPost]
    [Route("resume")]
    public async Task<IResult> ResumeWorkflowFromBookmark()
    {
        var instanceId = await GetDatabaseInstanceId(DefinitionId);
        var activityInstanceId = await GetFaultedActivityInstanceIdAsync(instanceId, Constants.FaultingEventActivityId);
        var bookmarkId = await GetMostRecentBookmarkId(instanceId, activityInstanceId!);
        var bookmarkFilter = new BookmarkFilter { BookmarkId = bookmarkId, WorkflowInstanceId = instanceId };
        var resumeBookmarkResult = await bookmarkResumer.ResumeAsync(bookmarkFilter);

        var msg = resumeBookmarkResult.Matched ? "Success" : "FAILED";
        
        return Results.Ok($"Bookmark Resume Result: {msg} for instance id: {instanceId}");
    }

    private async Task<string> GetDatabaseInstanceId(string definitionId)
    {
        var filter = new WorkflowInstanceFilter
        {
            DefinitionId = definitionId, WorkflowStatus = WorkflowStatus.Running,
            WorkflowSubStatus = WorkflowSubStatus.Suspended
        };
        var sort = new WorkflowInstanceOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Descending);
        var matches = await workflowInstanceStore.FindManyAsync(filter, sort);
        return matches.First().Id;
    }
    
    private async Task<string?> GetFaultedActivityInstanceIdAsync(string workflowInstanceId, string activityId)
    {
        // find the activityInstanceId from the `WorkflowExecutionLogRecords` table
        var sort = new WorkflowExecutionLogRecordOrder<DateTimeOffset>(x => x.Timestamp,
            OrderDirection.Descending);
        var filter = new WorkflowExecutionLogRecordFilter
        {
            WorkflowInstanceId = workflowInstanceId, ActivityId = activityId,
            EventName = "Faulted"
        };
        var logRecord = await workflowExecutionLogStore.FindAsync(filter, sort, default);
        if (logRecord is null)
            throw new Exception(
                $"Unable to find faulted activity instance ID for instance: {workflowInstanceId}, activity: {activityId}"); 
        return logRecord.ActivityInstanceId;
    }

    private async Task<string> GetMostRecentBookmarkId(string instanceId, string activityInstanceId)
    {
        var filter = new BookmarkFilter { WorkflowInstanceId = instanceId, ActivityInstanceId = activityInstanceId };
        var bookmark = await bookmarkStore.FindAsync(filter);
        if (bookmark is null) throw new Exception($"Unable to find bookmark ID for instance: {instanceId}, activity instance {activityInstanceId}");
        return bookmark.Id;
    }
    
}
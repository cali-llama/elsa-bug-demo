using System.Reflection.Metadata;
using Elsa.Expressions.Models;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.IncidentStrategies;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime.Activities;

namespace ElsaServerBugDemo;

public class FaultingBookmarkWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WorkflowOptions.IncidentStrategyType = typeof(ContinueWithIncidentsStrategy);
        var flowStepOne = new Sequence
        {
            Activities =
            {
                new WriteLine("Step 1, Pre Event"),
                new FaultingEvent("Resume"){ Id = Constants.FaultingEventActivityId },
                new WriteLine("Step 1, Post Event")
            }
        };

        var flowStepTwo = new Sequence
        {
            Activities =
            {
                new WriteLine("Step 2, First Activity")
            }
        };
        
        builder.Root = new Flowchart
        {
            Activities = { flowStepOne, flowStepTwo },
            Connections = { new Connection(flowStepOne, flowStepTwo) }
        };
    }
}

public class FaultingEvent : Event
{
    public FaultingEvent(string eventName, string? source = default, int? line = default) : base(eventName, source, line)
    {
    }

    public FaultingEvent(Func<string> eventName, string? source = default, int? line = default) : base(eventName, source, line)
    {
    }

    public FaultingEvent(Func<ExpressionExecutionContext, string?> eventName, string? source = default, int? line = default) : base(eventName, source, line)
    {
    }

    public FaultingEvent(Variable<string> variable, string? source = default, int? line = default) : base(variable, source, line)
    {
    }

    public FaultingEvent(Literal<string> literal, string? source = default, int? line = default) : base(literal, source, line)
    {
    }

    public FaultingEvent(Input<string> eventName, string? source = default, int? line = default) : base(eventName, source, line)
    {
    }

    protected override ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        // check environment variable to see if we should exception or continue as normal
        var value = Environment.GetEnvironmentVariable(Constants.FaultWorkflowEnvVar);
        if (value is not null && value == "1")
        {
            throw new Exception("Faulting Event...wait for it...FAULTED!");
        }
        return base.ExecuteAsync(context);
    }
}
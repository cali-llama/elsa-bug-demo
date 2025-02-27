# elsa-bug-demo
## Overview

This repo/branch was created to exhibit the bug behavior from Elsa v.3.3.1,
as described in the GitHub issue: https://github.com/elsa-workflows/elsa-core/issues/6458

## Prerequisites
This project is setup to target a Postgres database with database name `Elsa`. This is to simulate the same data persistence that is used in our enterprise product.

You can run your own Postgres docker container with...
   - `docker pull postgres` 

and after setting up a local folder to retain your data...
   - `docker run --name postgres-db -e POSTGRES_USER=root -e POSTGRES_PASSWORD='password' -e POSTGRES_DB=Elsa -v "{POSTGRES_FOLDER}":/var/lib/postgresql/data -p 5432:5432 -d postgres`

## Run Steps

1. Run the `http` launch profile to start up the Elsa web server.
2. Exercise the `http://localhost:5151/workflow/start` api endpoint. This will...
    - This will trigger the workflow defined in `FaultingBookmarkWorkflow`.
    - Based on the `FaultWorkflow` environment variable (set on the launch profile), this will cause the `FaultingEvent` activity to throw an exception the first time through.
    - If running in Debug mode, resume on exception and let the workflow fault the activity.
    - After the first execution burst, the endpoint will create an alteration to reschedule the faulted activity. This time around the activity should succeed and the endpoint will return.
3. Exercise the `http://localhost:5151/workflow/resume` api endpoint. This will...
    - lookup the latest `WorkflowInstanceId` and `BookmarkId` and resume the workflow from the `FaultingEvent` activity.
    - the workflow will execute the rest of the sequence, i.e., "Step 1, Post Event" `Writeline` activity and then suspend itself without completing the workflow.

## Expected Behavior

If instead we target v3.3.0, the workflow will behave as expected and run the second `Flowchart` sequence on resume and finish the workflow.





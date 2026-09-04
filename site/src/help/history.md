# History

A single timeline of every commit the pipeline has recorded against tracked
objects, most recent first.

## What is in the list

Each row is one commit: its timestamp, message, the number of objects it
touched and its short sha. Click a row to expand the objects that commit
changed - each links straight to its detail page.

This is git history mined from the repository the extractor writes into, so
a "commit" is one sync run's worth of DDL change, not a database
transaction. What lands in a commit is whatever actually differed since the
previous run.

## The commit window

History is mined by the `analyze-catalog` CI stage and bounded to a
configurable commit window, so the list is deliberately not the repository's
entire history. If the page reports no history at all, that stage ran
without `-RepoRoot` and mined nothing for this snapshot.

## Where to go from here

- For one object's history - including a point-in-time view of its
  definition and a side-by-side diff of any two revisions - open the object
  and use its **Change history** panel.
- For change *patterns* rather than a raw list - what changes most often,
  what tends to change together, activity by type over time - use the
  Overview page.

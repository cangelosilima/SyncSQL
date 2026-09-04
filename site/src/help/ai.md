# AI filter assistant

Turns an English request into a preview of Explorer filters plus, at most,
one DDL content query. It is a filter builder - not a chatbot, and not a
query engine.

## What actually runs

Everything happens in this browser. The quantized `all-MiniLM-L6-v2` model
is served from the same origin as the site and is fetched on your first
request, not at page load. Nothing you type leaves the machine, and no
request reaches a database.

The model is used for one narrow job: classifying ambiguous filter intent.
A deterministic, catalog-aware planner resolves the actual values and
operators against objects that exist in the snapshot. **The assistant never
generates or executes SQL.**

## Writing a request

Ask for objects by the attributes Explorer already filters on - server,
database, schema, type, name, description - and for terms that should appear
in an object's body:

```
Show stored procedures in AppDb that mention Orders
Find tables on SQLPROD01 except schema dbo
Objects named Customer that reference invoices
```

Requests are interpreted in English. The three example buttons fill the box
for you.

## Reading the preview

- **Filter chips** - exactly the tokens that will be handed to Explorer.
  Read them before opening; they are the real filter, the prompt is not.
- **Match count** - how many objects the plan selects out of the catalog.
- **Confidence** - how sure the planner is of its reading of the request.
- **Warnings** - parts that were interpreted loosely.
- **Unsupported fragments** - parts that could not be turned into a safe
  filter. **Open in Explorer** stays disabled until you rewrite those, so a
  half-understood request never quietly becomes a wrong filter.

**Open in Explorer** hands the plan over as ordinary URL filter tokens. From
there it is a normal Explorer view - editable, sortable and shareable.

## When the tab is disabled

The model is optional. If a deployment was built without it, the AI tab
stays visible but inert and the page explains which case applies - the model
was omitted, its Git LFS object was unresolved, it failed integrity
verification, packaging failed, or the browser could not initialize it. Only
the last case is retryable, with the button on the page. Every other feature
of the site is unaffected.

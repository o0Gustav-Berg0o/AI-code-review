# Diff-tjänst för AI-kodgranskning

Azure Function (.NET 8 isolated worker, DiffPlex) som returnerar unified diff för BEFORE/AFTER.
Se "New Textdokument.txt" för specifikation.

    cd src
    dotnet test
    cd DiffService.Function && func start

`POST /api/diff` med `{ "before": "...", "after": "...", "context": 8 }` och header `x-functions-key`.

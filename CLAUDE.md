# GrocerEase

A learning project on Clean Architecture (.NET 10, ASP.NET Core, PostgreSQL 18). Every step is documented in an Obsidian note, so the note is as much a deliverable as the code.

## Architecture

Dependencies point inward. A layer may reference only the layers shown here, and the architecture tests enforce it.

```
Domain  ←  Application  ←  Infrastructure
                ↑                ↑
                └──────  Api  ───┘──→  Contracts
```

- `Domain` references nothing. `Contracts` holds the public request and response types.
- `Api` is the composition root: it is the only project that references `Infrastructure`.
- A new project reference updates the architecture test in the same commit. A new convention (for example "handlers are sealed", "handlers return a `Result`") gets its own `test(architecture)` commit in the same PR, right after the code it constrains.
- **Errors:** an expected business failure (duplicate email, wrong credentials, not found) is returned as a `Result<T>` with a `ResultError`; the Api maps its `ErrorType` to a status. Malformed input throws `ValidationException` from `ValidationDecorator`. Anything unexpected is an exception that `UnhandledExceptionHandler` logs and turns into a generic `500`. All error responses are `application/problem+json`.

## Workflow

Work in this order. A task is done when every step has finished.

1. **Branch.** `main` is protected, so work on a branch (`feat/…`, `fix/…`, `ci/…`, `docs/…`). Run `gh auth status` before the first `gh` command: a wrong account fails with `must be a collaborator`.
2. **Build the change.** Write a unit test, an integration test and an architecture test for every change that can be tested (`tests/GrocerEase.UnitTests`, `.IntegrationTests`, `.ArchitectureTests`).
3. **Iterate on the new test only:** `dotnet test --filter "FullyQualifiedName~<TestName>"`.
4. **Commit in small slices.** One behaviour per commit, and each commit passes its own tests. Use Conventional Commits: `<type>(<optional scope>): <imperative, lowercase subject>`, with type one of `feat`, `fix`, `test`, `refactor`, `docs`, `build`, `ci`, `chore`.
5. **Run the full suite** (`dotnet test`) once, before the final commit. Integration tests need Docker running.
6. **Open a PR** with `gh pr create`. It can merge when both checks pass: `Build and test` and `Docker image and Compose file`. The author merges it, with *rebase and merge*.
7. **Write the note** once the PR checks are green, before the merge. Topic headings cite the PR number and commit number, not hashes (see Notes).
8. **After the merge.** Do this when the author has merged. `git checkout main && git pull --ff-only`. Check that the branch and `main` have the same tree (`git diff --quiet <branch> main`), then delete the branch locally with `git branch -D` (`-d` refuses, because a rebase merge gives every commit a new hash) and on the remote with `git push origin --delete <branch>`. Then replace the hashes in the note's `## Commits` table with the ones on `main`, matched by commit subject. Do not keep the old branch hashes or a mapping table.

**Done means:** the full suite is green, the build has no warnings (they are errors), the PR checks pass, the PR is merged, the branch is deleted locally and on the remote, and every hash in the note's `## Commits` table appears in `git log main`.

## Build and run

- **Package versions** live in `Directory.Packages.props`; a `.csproj` carries no version. Shared properties live in `Directory.Build.props`, and the SDK is pinned in `global.json`.
- **Run the stack:** `cp .env.example .env`, set a real `POSTGRES_PASSWORD`, then `docker compose up --build`. The API listens on `http://localhost:8080` and PostgreSQL on `127.0.0.1:5432`. The README lists the other Compose commands.
- **PostgreSQL 18** keeps its data under a different volume path than earlier versions; note 4 explains it.

## Notes

Location: `~/Documents/Guides to Great SW/Better NCC DEV/Applications/GrocerEase`

The reader is the author, learning the stack. Explain **why** each choice was made, not only what changed.

- **File name:** `<N>. <Title>.md`, where `<N>` is one more than the highest existing number in this folder.
- **Commit references:** every topic heading cites the PR and the number (the `#` column of the `## Commits` table) of the commit it describes, e.g. `## The workflow (PR #5, commit 1)`. A topic spanning several commits lists each number, e.g. `(PR #5, commits 2-3)`. The `## Commits` table is the **only** place with hashes, so a rebase merge, which gives every commit a new hash, means rewriting one table. Notes written before this rule keep their hashes in headings.
- **Structure:** follow the latest note: `## Commits`, one section per topic, `## Choices made`, `## Verifying it`, `## Where it stands`. Notes live outside the repo and are not committed.
- **Scope:** a step note covers its own slice. When a step needs a concept (a language feature, a pattern), give the concept its own short note and link to it instead of explaining it inline.
- **Concept notes:** `~/Documents/Guides to Great SW/Better NCC DEV/<N>. <Title>.md`, numbered separately from the step notes (`<N>` is one more than the highest number in that folder).
- **`Next Plan.md`:** an unnumbered roadmap in this folder. When a slice is done, tick its boxes and add what the slice taught.
- **`dotnet` CLI commands:** when a change used one (`dotnet new`, `dotnet add package`, `dotnet sln add`, `dotnet test --filter`, …), list the exact command in the note next to the topic it served, with a line on what each flag does.
- **Verifying it:** show the real commands and their real output. Report a failure as a failure.
- **Ending:** close every note with a `## Where it stands` checklist: `- [x]` for what is done and verified, `- [ ]` for what is missing or can be improved.
- **Q&A:** a question the author asks while learning can be saved as its own note in `Q&A/`, named after the question.

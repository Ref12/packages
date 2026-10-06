# packages

Package publishing for ref12labs: GitHub Releases used as an anonymous NuGet v3 feed (registration index only).

Source URL: `https://github.com/ref12labs/packages/releases/download/feed/index.json`

## Layout

| Release | Holds |
|---|---|
| `feed` | the service index (`index.json`), with `RegistrationsBaseUrl/3.6.0` = `https://github.com/ref12labs/packages/releases/download/` |
| `<id>` (lowercase id) | `index.json`: the generated registration index of that package, inline items |
| `<id>-<version>` | `<id>.<version>.nupkg`, `<id>.<version>.nuspec`, and an `unlisted` marker when unlisted |

No slash tags (git cannot have tag `x` and `x/1.0`). Ids and versions are lowercase in names; versions are NuGet-normalized (`1.0.0.0` -> `1.0.0`, build metadata dropped).

## Consume

`nuget.config` (next to your solution). `packageSourceMapping` makes only the allowed ids come from here, so nobody can shadow them from another feed, and nothing else is asked of this feed:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="ref12" value="https://github.com/ref12labs/packages/releases/download/feed/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="ref12">
      <package pattern="Ref12.FeedProbe" />
      <package pattern="Ref12.Wasm*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Keep the patterns in line with `packages.json`. No credentials are needed; downloads follow GitHub's 302 to signed asset URLs.

## Publish

A package may be published only if its id and source repo are in `packages.json`:

```json
{ "packages": [
  { "id": "Ref12.FeedProbe", "repos": ["ref12labs/packages"], "nugetOrg": false },
  { "prefix": "Ref12.Wasm", "repos": ["ref12labs/dotnet-wasm-lab"], "nugetOrg": false, "todo": "..." }
] }
```

An exact `id` entry wins over prefixes; prefixes are plain case-insensitive "starts with" (`Ref12.Wasm` also matches `Ref12.WasmSdk`). `nugetOrg` must be `true` before `nuget_org: true` is accepted for that id. Change the file by pull request.

### Release source: a .nupkg attached to a release of a source repo

```sh
gh workflow run publish.yml -R ref12labs/packages \
  -f repo=ref12labs/some-lib -f tag=v1.2.3 -f asset=Some.Lib.1.2.3.nupkg
```

### Build source: build it here with `dotnet pack`

```sh
gh workflow run publish.yml -R ref12labs/packages \
  -f repo=ref12labs/packages -f ref=main -f project=probe/Ref12.FeedProbe
```

Add `-f nuget_org=true` to also push to nuget.org (see below).

### From another repo's release workflow

Needs a secret `PACKAGES_DISPATCH_TOKEN` in that repo: a fine-grained token limited to `ref12labs/packages` with *Contents: read and write* (required by the dispatch API). `repository_dispatch` only runs the workflow on the default branch.

```yaml
on:
  release:
    types: [published]
jobs:
  publish-package:
    runs-on: ubuntu-latest
    steps:
      # ...your steps that build the .nupkg and attach it to the release as Some.Lib.${{ github.event.release.tag_name }}.nupkg...
      - name: Ask ref12labs/packages to publish it
        env:
          GH_TOKEN: ${{ secrets.PACKAGES_DISPATCH_TOKEN }}
        run: |
          gh api repos/ref12labs/packages/dispatches \
            -f event_type=publish \
            -f 'client_payload[repo]=${{ github.repository }}' \
            -f 'client_payload[tag]=${{ github.event.release.tag_name }}' \
            -f 'client_payload[asset]=Some.Lib.*.nupkg'
```

`client_payload` keys are the workflow inputs: `repo`, `tag` + `asset` or `ref` + `project`, optional `nuget_org`. Private source repos need a `SOURCE_REPO_TOKEN` secret here (read access to contents); public repos need nothing.

## Guardrails

- The package is read, not trusted: **id and version come from the nuspec** inside the nupkg, never from the inputs or the file name.
- Release source: the **tag must equal the nuspec version** (`v1.2.3` or `1.2.3`).
- The source repo must be listed in `packages.json` *before* anything is fetched or built, and the id must be allowed for that repo *after* the nuspec is read.
- Fetch/build runs in a read-only job; only the later job holding `contents: write` touches releases.
- **Versions are immutable**: if release `<id>-<version>` exists the publish fails. Publish a new version, or unlist.
- One index rebuild at a time per package id (`concurrency: index-<id>`). The version release is created before the index job queues, and each index run rebuilds from all releases, so a queued run superseded by a newer one loses nothing. If an index job fails, run `reindex`.
- nuget.org only through Trusted Publishing (OIDC, short-lived key); there is no API key secret anywhere.

## Maintain

- **Unlist** (hide from search, keep downloadable): `gh workflow run unlist.yml -R ref12labs/packages -f id=Ref12.FeedProbe -f version=0.0.2`. Add `-f relist=true` to undo. This adds/removes the `unlisted` asset and regenerates the index. NuGet still restores an exact unlisted version.
- **Reindex** (repair): `gh workflow run reindex.yml -R ref12labs/packages -f id=Ref12.FeedProbe`; without `id` it rebuilds every package index and the service index. Releases made before nuspec assets existed are read from their nupkg.

## Enable nuget.org (not active until you do this)

1. On nuget.org: sign in as the owner account, then *Trusted Publishing* (https://www.nuget.org/account/trustedpublishing) -> add a policy:
   - Repository Owner: `ref12labs`
   - Repository: `packages`
   - Workflow File: `publish.yml`
   - Environment: `nuget-org`
2. In GitHub (ref12labs/packages) -> Settings -> Environments: create `nuget-org` (optionally with required reviewers).
3. Set the variable `NUGET_USER` to your nuget.org **profile name** (not email): Settings -> Secrets and variables -> Actions -> Variables (repository, or on the `nuget-org` environment).
4. Set `"nugetOrg": true` on the package's entry in `packages.json`.
5. Publish with `-f nuget_org=true`.

Until `NUGET_USER` is set the nuget.org job prints a notice and skips. Docs: https://learn.microsoft.com/nuget/nuget-org/trusted-publishing

## Tools and tests

- `tools/Feed`: .NET 8 console app (not a single-file app, so tests can reference it). `dotnet run --project tools/Feed -- verify|allowed-repo|index ...`
- `tools/reindex.sh`: collects release metadata with `gh`, runs the tool, uploads indexes.
- `dotnet test tests/Feed.Tests`: version ordering, index generation on fixtures (versions, prereleases, dependency groups, unlisted, paging), allowlist and verify checks, and a real `dotnet restore` from a local feed built by the generator and served over HTTP with a GitHub-style 302. Runs on ubuntu and windows (`test.yml`).
- `restore-test.yml`: restores the probe from the live feed on ubuntu and windows.
- `feed-b/`, `probe/`: the original experiment and the probe package.

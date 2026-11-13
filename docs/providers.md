# Release hosts

Quiver installs and updates apps from a release host: a forge's releases page, or a JSON feed. The launcher asks the
host's API directly when it allows cross-origin requests, and through a small Rust proxy when it doesn't.

## Supported hosts

| Host                                | Provider id | Releases       | README | Tokens                          |
| ----------------------------------- | ----------- | -------------- | ------ | ------------------------------- |
| GitHub (github.com)                 | `github`    | yes            | yes    | PAT, `Authorization: Bearer`    |
| GitHub Enterprise                   | `github`    | yes            | yes    | PAT                             |
| GitLab (gitlab.com)                 | `gitlab`    | yes            | yes    | PAT, `PRIVATE-TOKEN`            |
| Self-hosted GitLab                  | `gitlab`    | yes            | yes    | PAT                             |
| Gitea / Forgejo / Codeberg          | `gitea`     | yes            | yes    | token, `Authorization: token`   |
| Bitbucket Cloud                     | `bitbucket` | downloads only | yes    | app password (Basic)            |
| JSON release feed                   | `feed`      | yes            | no     | none                            |

Codeberg (`codeberg.org`), Gitea's own instance (`gitea.com`), Ryubing (`git.ryujinx.app`) and Eden
(`git.eden-emu.dev`) are known by name. Any other Gitea/Forgejo host is detected by asking
`https://<host>/api/v1/version`.

Gogs and other forges that copied GitHub's API can use **Gitea / Forgejo**: their releases are read from
`/api/v1/repos/<owner>/<repo>/releases`. A host that doesn't answer `/api/v1/version` isn't detected, so pick
**Gitea / Forgejo** yourself.

## Adding a repository

1. Add an app and choose **From a git repository**.
2. Paste a repository address (`https://codeberg.org/owner/repo`, `git.example.com/owner/repo`, or `owner/repo` for
   GitHub) or a feed URL.
3. Leave **Detect it** selected, or pick the host kind yourself when detection can't reach it.
4. Pick the app's name and, when a release has several files for this computer, the file that belongs to the app.

The library key keeps the host only when it isn't the provider's default:

- `github:owner/repo`
- `gitlab:group/project`
- `gitea:codeberg.org/owner/repo`
- `gitlab:git.example.com/group/project`

## Tokens

Tokens are per host, kept in the OS keychain, and sent only to that host. Settings → Advanced has GitHub, GitLab and
**Other repository tokens**. A token is only needed for private repositories or higher rate limits.

- GitHub: a classic token with no scopes, or a fine-grained token with read access.
- GitLab: `read_api`.
- Gitea/Forgejo: `repository: Read` (and `user: Read` on some versions).
- Bitbucket: an app password with `Repositories: Read`, entered as `username:app-password`.

## Checksums and updates

- GitHub publishes a SHA-256 digest for release assets. Quiver uses it when it's there.
- GitLab, Gitea/Forgejo and Bitbucket don't publish per-file checksums. Those apps show **Quiver hasn't checked this
  app**, as GitLab apps already did.
- Catalog apps are verified by quiverlauncher.com; that verification is separate from the host's own checksums.
- Bitbucket has no per-tag releases. Quiver groups its Downloads by the version in each file's name, so update
  detection is best effort.

## Self-hosted hosts and the local network

The Rust proxy refuses `http://` and private, loopback, link-local and documentation addresses. A forge on a LAN
(`https://192.168.1.10`, `https://git.local`) won't work yet. A host needs a public HTTPS address with a certificate
the system trusts.

## Account sync

The account stores GitHub and GitLab apps. Apps added from other hosts stay on the computer that added them; the
add-app dialog says so.

## Release feed format

A project with no release API can publish a JSON file:

```json
{
  "releases": [
    {
      "version": "1.2.3",
      "releasedAt": "2026-01-01T00:00:00Z",
      "prerelease": false,
      "notes": "What changed",
      "assets": [
        {
          "name": "app-linux-x64.zip",
          "url": "https://example.com/app-linux-x64.zip",
          "size": 123456,
          "sha256": "…64 hex characters…"
        }
      ]
    }
  ]
}
```

`releasedAt` may be an ISO 8601 string or a Unix timestamp. `os`, `architecture` and `format` are inferred from `name`
when they're absent. `sha256` is optional; without it the app is unchecked.

## Adding a provider

Providers live in `packages/api/src/providers/`. A provider implements `RepositoryProvider` (`types.ts`): `releases`,
optional `readme`/`rateLimit`, `webUrl`, and optional token help. Register it in `registry.ts` and add its known hosts.
The launcher's UI picks it up from `PROVIDERS`; no page needs to know its name.

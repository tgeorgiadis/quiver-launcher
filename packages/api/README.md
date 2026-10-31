# @quiverlauncher/api

A typed client for the [quiverlauncher.com](https://quiverlauncher.com) catalog API
(`https://api.quiverlauncher.com/api/v1`), and the types of what it returns. Quiver Launcher uses it, and
[`@quiverlauncher/ui`](../ui)'s components take its `Entry`.

```ts
import { createClient } from "@quiverlauncher/api";

const catalog = createClient();
const { items } = await catalog.apps({ search: "zelda" });
const app = await catalog.app(items[0].slug);
```

When `api.quiverlauncher.com` can't be reached at all, the default client asks the same API on the Convex
deployment's own host (`FALLBACK_API`) from then on, as Quiver Launcher 3 does; `onFallback` hears about it.

The types follow the site's public responses (also described at `/api/v1/openapi.json`); fields the client
doesn't know are ignored. It also has small helpers the launcher uses: `inferPlatform` for release file names,
`parseRepository` for GitHub and GitLab addresses, and `listSlug`/`listUrl` for shared lists.

All rights reserved. See the repository's [LICENSE](../../LICENSE).

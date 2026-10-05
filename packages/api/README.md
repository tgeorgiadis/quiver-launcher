# @quiverlauncher/api

A typed client for the [quiverlauncher.com](https://quiverlauncher.com) catalog API
(`https://api.quiverlauncher.com/api/v1`), and the types of what it returns. Quiver Launcher uses it, and
[`@quiverlauncher/ui`](https://www.npmjs.com/package/@quiverlauncher/ui)'s components take its `Entry`.

```ts
import { createClient } from "@quiverlauncher/api";

const catalog = createClient();
const { items } = await catalog.apps({ search: "zelda" });
const app = await catalog.app(items[0].slug);
```

The types follow the site's public responses (also described at `/api/v1/openapi.json`); fields the client
doesn't know are ignored. It also has small helpers the launcher uses: `inferPlatform` for release file names,
`parseRepository` for GitHub and GitLab addresses, and `listSlug`/`listUrl` for shared lists.

MIT licensed.

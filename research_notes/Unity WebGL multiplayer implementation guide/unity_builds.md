# Unity 6 WebGL and Linux Dedicated Server Builds: Editor, CLI, Hosting

IMPORTANT sourcing caveat: WebFetch to docs.unity3d.com and docs.unity.com failed (DNS ENOTFOUND) for every attempt. Everything marked "Cited" below comes from WebSearch result summaries only (not full pages). Everything marked "UNVERIFIED" is from my own prior knowledge (training data) and must be tested on the real project (Unity 6000.6.6f1) before being relied on. Code blocks are all UNVERIFIED unless stated otherwise (none was compiled here; no Unity editor available).

## 1. Install requirements (Unity Hub modules, cross-building Linux server)

### Takeaway
For the Linux server you need the Linux Dedicated Server Build Support module; for IL2CPP you also need Linux Build Support (IL2CPP); Mono is simplest. WebGL needs WebGL Build Support. Cross-building Linux IL2CPP from Windows/macOS is supported via Unity's sysroot toolchain, but a Unity issue-tracker bug on IL2CPP Linux server builds exists.

### Cited Findings
- Unity's Linux IL2CPP guide says you must install the Linux Build Support (IL2CPP) module through Unity Hub; the sysroot toolchain lets you build Linux IL2CPP from Linux, macOS and Windows — [Unity sysroot package manual](https://docs.unity3d.com/Packages/com.unity.sysroot@2.0/manual/index.html) (search summary)
- Edgegap's setup guide lists the modules: Linux Build Support (IL2CPP), Linux Build Support (Mono), Linux Dedicated Server Build Support — [Edgegap Unity docs](https://docs.edgegap.com/unity) (search summary)
- No source found saying the Dedicated Server module alone suffices for IL2CPP — same search.
- Issue tracker: "Dedicated Linux server build fails when using IL2CPP in specific projects" (UUM-121029), reproduced on Windows 11 with 2022.3.67f1 and 6000.x betas, link-time undefined symbol. Fixed status in 6000.6 unknown — [Unity Issue Tracker](https://issuetracker.unity3d.com/issues/dedicated-linux-server-build-fails-when-using-il2cpp-in-specific-projects) (search summary)
- Unity defines `UNITY_SERVER` when building the Dedicated Server platform; macOS server builds should be code-signed — [Unity 6000.6 Dedicated Server build](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/dedicated-server/build) (search summary)

### Inferences
- Recommended modules in Unity Hub for 6000.6.6f1: WebGL Build Support; Linux Build Support (Mono); Linux Dedicated Server Build Support; add Linux Build Support (IL2CPP) only if IL2CPP is wanted. Start with Mono for the server (UNVERIFIED but low-risk; avoids the IL2CPP link bug and sysroot dependencies).
- Dedicated Server build support is also a module for Windows/macOS server targets (Windows Dedicated Server, Mac Dedicated Server); only the Linux one is needed here (UNVERIFIED, from memory).

### Gaps
- Exact Hub module names in 6000.6 (labels may differ slightly); whether UUM-121029 is fixed in 6000.6.6f1; whether Mono Linux server needs no extra toolchain on Windows (believed yes, unverified).

## 2. WebGL Player Settings

### Takeaway
Settings are scriptable through `PlayerSettings.WebGL.*`. Cited facts confirm the properties exist; recommended values below are my own unverified suggestions. Key rule: the Compression Format you pick must match what the web server sends (Content-Encoding).

### Cited Findings
- Compression Format: Gzip is default (larger files than Brotli, faster build, natively supported over HTTP and HTTPS); Brotli gives best ratio; setting applies only to release builds — [Unity Manual: Deploy a Web application](https://docs.unity3d.com/Manual/webgl-deploying.html) (search summary)
- Server must send a Content-Encoding matching the build compression so browsers decompress during download — same source.
- Decompression Fallback embeds a JavaScript decompressor into the build, used when the browser does not decompress natively; native is faster for startup — same source.
- `PlayerSettings.WebGL` exposes: `initialMemorySize` (MB, initial WASM heap), `maximumMemorySize` (MB), `memoryGrowthMode` (linear step / geometric factor, with geometric step cap), `dataCaching` (automatic caching of unityweb files), `nameFilesAsHashes` (MD5 of uncompressed contents as filename) — [PlayerSettings.WebGL scripting reference](https://docs.unity3d.com/ScriptReference/PlayerSettings.WebGL.html) and [6000.6 version](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityeditor/playersettings/webgl) (search summary; defaults not shown)
- Compression Format set at Project Settings > Player > WebGL > Publishing Settings — [Unity forum / itch.io threads](https://discussions.unity.com/t/unity-export-webgl-error-unable-to-parse-build-webgame-framework-js-unityweb-the-file-is-corrupt-or-compression-was-misconfigured/305236)

### Inferences / UNVERIFIED recommendations for a small multiplayer game
- Compression: Gzip for the simplest path (works on any server with correct headers); Brotli over HTTPS only (browsers only accept `Content-Encoding: br` on secure contexts) — the "Brotli needs HTTPS" point is from memory, partly supported by the "Gzip works on HTTP and HTTPS natively" cited line.
- Decompression Fallback: ON while testing/unknown hosting (build is bigger, +JS decompressor); OFF once nginx headers are verified.
- Name Files As Hashes: ON (enables long immutable caching of Build/ files). Data Caching: ON (IndexedDB caching), or off if you iterate quickly and see stale builds.
- Code Optimization: Release builds use "Runtime Speed with LTO" / "Disk size with LTO"; for small game choose Disk Size (UNVERIFIED names). Managed Stripping Level: Medium/High (High may break reflection; Netcode for GameObjects uses codegen/reflection, so keep Low or Medium and add `link.xml` if needed). Strip Engine Code: ON. Exception Support: "Explicitly Thrown Exceptions Only" or None for release; "Full" only for debugging.
- Memory (Unity 6): Initial 32–256 MB, Maximum 512–2048 MB, growth mode Geometric; small maze game: initial 64, max 512 (UNVERIFIED guess; tune with profiler).
- WebAssembly 2023 option (Unity 6: enables newer WASM features, may exclude older browsers e.g. Safari versions) — UNVERIFIED; keep OFF for max compatibility.
- Run In Background: ON for multiplayer so the socket/transport keeps ticking when the tab is unfocused (`PlayerSettings.runInBackground = true`), noting browsers still throttle timers in background tabs.
- Linker target / templates: Unity 6 emits WebAssembly only (asm.js removed). Custom template via `Assets/WebGLTemplates/<Name>/index.html` and `PlayerSettings.WebGL.template = "PROJECT:<Name>"` (UNVERIFIED, from memory). Canvas size set in template CSS/`config`.
- Publishing: `PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;` `.decompressionFallback`, `.nameFilesAsHashes`, `.dataCaching`, `.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly`.

### Gaps
- Default values and exact UI labels in 6000.6 could not be fetched. Netcode transport on WebGL requires WebSocket transport (outside this scope).

## 3. WebGL output layout and HTTP headers

### Takeaway
Output folder contains `index.html`, `Build/<name>.loader.js`, `.framework.js`, `.data`, `.wasm` (with `.gz`/`.br` suffix when compressed; `.unityweb` suffix only when Decompression Fallback is on / for Disabled+fallback cases) and `TemplateData/`. Server must map suffixes to Content-Encoding and original MIME types.

### Cited Findings
- Unity's server samples: for pre-compressed `.js.br`, `.wasm.br`, and data files the nginx sample turns off dynamic gzip, sets `Content-Encoding br`, and uses correct MIME type (`application/wasm` for WebAssembly); IIS sample says disable static compression to avoid double compression — [Unity server configuration code samples](https://docs.unity3d.com/Manual/webgl-server-configuration-code-samples.html) (search summary; sample text not retrieved; samples seen were from 2020–2023 versions, Unity 6 page not confirmed identical)
- Brotli uses `.br` and `Content-Encoding: br` — [Unity Manual webgl-deploying](https://docs.unity3d.com/Manual/webgl-deploying.html) (search summary)

### Inferences / UNVERIFIED config (written from memory; test with `curl -I`)

Expected Unity 6 files (Gzip): `Build/X.loader.js` (never compressed), `Build/X.framework.js.gz`, `Build/X.data.gz`, `Build/X.wasm.gz`. Brotli: same with `.br`.

nginx, precompressed, Gzip build (UNVERIFIED), inside the server block:
```nginx
root /var/www/maze/webgl;
index index.html;
gzip off;   # don't double-compress precompressed files

location / { try_files $uri $uri/ =404; }

location ~* \.data\.gz$      { default_type application/octet-stream; add_header Content-Encoding gzip; }
location ~* \.wasm\.gz$      { default_type application/wasm;         add_header Content-Encoding gzip; }
location ~* \.js\.gz$        { default_type application/javascript;   add_header Content-Encoding gzip; }
location ~* \.symbols\.json\.gz$ { default_type application/octet-stream; add_header Content-Encoding gzip; }

# Hashed filenames (Name Files As Hashes ON): cache hard
location /Build/ { add_header Cache-Control "public, max-age=31536000, immutable"; }
location = /index.html { add_header Cache-Control "no-cache"; }
```
Caveat: nginx `add_header` inside a location replaces headers inherited from outer blocks, and a Cache-Control in `/Build/` is overridden by more-specific regex locations (regex locations win over prefix `/Build/`), so duplicate the Cache-Control line into each regex location. Brotli: replace `.gz` with `.br` and `gzip` with `br`. Alternative: the `gzip_static on;` / `brotli_static` module approach with uncompressed names only works if you disable Unity's compression.

Caddy (UNVERIFIED), Caddyfile:
```caddy
maze.example.com {
    root * /var/www/maze/webgl
    @gz path *.js.gz *.wasm.gz *.data.gz
    @wasm path *.wasm.gz *.wasm.br
    @data path *.data.gz *.data.br
    @js   path *.js.gz *.js.br
    header @gz Content-Encoding gzip
    header @wasm Content-Type application/wasm
    header @data Content-Type application/octet-stream
    header @js   Content-Type application/javascript
    @br path *.js.br *.wasm.br *.data.br
    header @br Content-Encoding br
    header /Build/* Cache-Control "public, max-age=31536000, immutable"
    header /index.html Cache-Control no-cache
    file_server
}
```
Caddy's `file_server` may auto-set Content-Type for `.gz` as `application/gzip`; the `header` directive with `defer`/`>` override semantics may be needed (`header @wasm >Content-Type application/wasm`) — flag for testing. Caddy provides automatic HTTPS, which satisfies Brotli's secure-context need. Also: the WebSocket path for NGO should be proxied to the game server (reverse_proxy / nginx `proxy_set_header Upgrade`), out of scope here.

Same-VM hosting: put static root on the Linux VM, TLS via Caddy or certbot; serve the dedicated server on a separate port behind `/ws` proxy for wss:// (an https page cannot open ws://). UNVERIFIED but standard browser mixed-content rule.

### Gaps
- Verbatim Unity 6 nginx sample not retrieved. Whether Unity 6 names compressed files `.unityweb` anywhere (older versions used it) — not confirmed.

## 4. Local WebGL testing

### Takeaway
file:// does not work; use a static HTTP server. Unity's Build And Run starts one.

### Cited Findings
- No cited source retrieved in this session for this item.

### Inferences / UNVERIFIED
- Browsers block fetch/XHR of wasm from file:// (CORS/origin), so loader fails. Unity's Build And Run launches a local server on localhost and opens the browser, serving compression headers itself.
- Manual: `cd Builds/WebGL && python3 -m http.server 8000` works only for Disabled compression (Python server sets no Content-Encoding); for gz/br builds either enable Decompression Fallback or use a server with the nginx/Caddy config above (or `npx http-server -g`/`-b` flags for gzip/brotli precompressed, UNVERIFIED).

### Gaps
- Unity 6 Build And Run behavior with Brotli on localhost (http without TLS) not verified.

## 5. Linux Dedicated Server build: editor and CLI

### Takeaway
In Unity 6 use Build Profiles with the Dedicated Server platform (Linux), or CLI flags `-buildTarget Linux64 -standaloneBuildSubtarget Server`, or script `EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server` plus `BuildPlayer` with `target = StandaloneLinux64`.

### Cited Findings
- Command line: `-buildTarget Linux64 -standaloneBuildSubtarget Server`; subtarget in some scripting contexts needs an integer value — [Unity 6000.6 Dedicated Server build](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/dedicated-server/build) (search summary, also docs.unity.cn mirror 6000.6 alpha)
- `UNITY_SERVER` symbol defined for Dedicated Server builds; runtime args via player command line and `DedicatedServer.Arguments` API — same source.

### Inferences / UNVERIFIED
- Editor: File > Build Profiles > Add Build Profile > "Linux" (Dedicated Server checkbox / "Linux Server" profile) > Switch Profile > Build. Dedicated Server strips graphics assets, audio, and uses `-batchmode -nographics` behavior by default; Unity 6 server builds run headless automatically but add `-batchmode -nographics -logFile -` explicitly to be safe.
- Mono vs IL2CPP: Mono needs no cross toolchain; IL2CPP needs the Linux IL2CPP module (see §1).
- Server output: `Builds/Server/<name>.x86_64` plus `<name>_Data/`; run `chmod +x`.
- Optimizations: Dedicated Server's "Server Build" sets Stripping of unused rendering assets automatically; also disable `Auto Graphics API`, set `Managed Stripping Level` Low–Medium.

### Gaps
- Build Profile exact UI naming and the `-activeBuildProfile` CLI flag in 6000.6 (I recall `-activeBuildProfile <path>` exists in Unity 6; unverified).

## 6. Editor build script (UNVERIFIED, uncompiled)

```csharp
// Assets/Editor/BuildScript.cs
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    static string Version => Environment.GetEnvironmentVariable("BUILD_VERSION")
                              ?? PlayerSettings.bundleVersion;
    static string[] Scenes() { var l = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes) if (s.enabled) l.Add(s.path); return l.ToArray(); }

    [MenuItem("Build/WebGL")] public static void BuildWebGL() => Run(BuildTarget.WebGL, false,
        Path.Combine("Builds", "WebGL", Version));
    [MenuItem("Build/Linux Server")] public static void BuildLinuxServer() => Run(BuildTarget.StandaloneLinux64, true,
        Path.Combine("Builds", "LinuxServer", Version, "maze-server.x86_64"));
    public static void BuildAll() { BuildWebGL(); BuildLinuxServer(); }

    static void Run(BuildTarget target, bool server, string output)
    {
        Directory.CreateDirectory(server ? Path.GetDirectoryName(output) : output);
        if (target == BuildTarget.WebGL)
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.runInBackground = true;
        }
        var group = server ? BuildTargetGroup.Standalone : BuildTargetGroup.WebGL;
        var opts = new BuildPlayerOptions {
            scenes = Scenes(), locationPathName = output, target = target,
            targetGroup = group, options = BuildOptions.None };
        if (server) {
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
            opts.subtarget = (int)StandaloneBuildSubtarget.Server;
        } else if (target == BuildTarget.StandaloneLinux64)
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        var report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("Build failed: " + report.summary.result);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw new Exception("Build failed");
        }
        Debug.Log($"Build OK {target} -> {output} ({report.summary.totalSize} bytes)");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
```
Notes: `BuildPlayerOptions.subtarget` (int) exists in Unity 2021.2+ from memory; `BuildTargetGroup.Standalone` plus `NamedBuildTarget.Server` is the newer API (`PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x)`). Exiting from inside the method conflicts with `-quit` in BuildAll (the first target would exit before the second); in the final version, only exit in an outer wrapper. Treat as a skeleton to compile and fix. Defines: `-buildTarget` should be switched before `-executeMethod` so the active target is right.

### Command lines (UNVERIFIED except flags noted)
Linux:
```
/opt/unity/6000.6.6f1/Editor/Unity -batchmode -nographics -quit \
  -projectPath "/path/to/multiplayer game" \
  -buildTarget Linux64 -standaloneBuildSubtarget Server \
  -executeMethod BuildScript.BuildLinuxServer -logFile build-server.log
```
WebGL: `-buildTarget WebGL -executeMethod BuildScript.BuildWebGL`.
Windows (PowerShell): `& "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\path\multiplayer game" -buildTarget WebGL -executeMethod BuildScript.BuildWebGL -logFile build.log`
macOS: `/Applications/Unity/Hub/Editor/6000.6.6f1/Unity.app/Contents/MacOS/Unity ...`.
Quote the project path because of the space in `multiplayer game`. `-logFile -` writes to stdout. Do not combine `-nographics` with WebGL builds (can fail; unverified, omit it). Do not run two editor instances on the same project (lock).

### Gaps
- None of this compiled. `-quit` plus `EditorApplication.Exit` interaction, and whether `-standaloneBuildSubtarget` sets the right NamedBuildTarget for PlayerSettings in 6000.6, must be tested.

## 7. Client/server match, scene lists, version protection

### Takeaway
Use one scene list and one shared `NetworkPrefabsList`/scripts for both builds; enforce a build version in Netcode connection approval.

### Cited Findings
- None retrieved in this session.

### Inferences / UNVERIFIED
- Both builds come from the same project and commit; `UNITY_SERVER` only for server-only code. Scene list: `EditorBuildSettings.scenes` (or Build Profile scene list in Unity 6; profiles can override the global list).
- NGO: `NetworkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Application.version)` on client; server `ConnectionApprovalCallback` compares `request.Payload` to its own `Application.version` and sets `response.Approved=false`, `response.Reason="Version mismatch"`. Also NGO's `NetworkConfig.ForceSamePrefabs` (default true) and config hash checks reject mismatched prefab lists. Set `BUILD_VERSION` from git SHA/tag in CI and write to `PlayerSettings.bundleVersion` before build.

### Gaps
- Not checked against NGO 2.x docs.

## 8. GameCI GitHub Actions (optional)

### Takeaway
GameCI v4 documents a Personal-license route using a `.ulf` created via Unity Hub plus secrets, but reports conflict on whether older manual activation still works; no 2026 confirmation found.

### Cited Findings
- GameCI docs: activate locally with Unity Hub to create a `.ulf` linked to your account; store `.ulf` and credentials as GitHub secrets; Hub may not create the `.ulf` unless you add a free personal license via Licenses > Add — [GameCI activation docs (proxy mirror)](https://proxy-dev.blitzz.co/proxy/123456/game.ci/docs/github/activation) (mirror, verify at game.ci)
- Unity forum: manual activation (alf to ulf at license.unity3d.com) of Personal licenses no longer supported — [Unity Discussions](https://discussions.unity.com/t/unity-no-longer-supports-manual-activation-of-personal-licenses/926760); workaround post [DEV Community](https://dev.to/ankursheel/workaround-for-unity-personal-license-manual-activation-not-supported-49j2) (unverified); related issue [game-ci/unity-orb#74](https://github.com/game-ci/unity-orb/issues/74)
- Unity docs: Personal users activate by signing into Hub; command-line serial activation works only for Plus/Pro — [Unity Hub licenses](https://docs.unity.com/hub/manage-license.html)

### Inferences / UNVERIFIED
- Workflow skeleton: `game-ci/unity-builder@v4` with `targetPlatform: WebGL` and `StandaloneLinux64` (+ `customParameters: -standaloneBuildSubtarget Server` and `buildMethod: BuildScript.BuildLinuxServer`), `unityVersion: 6000.6.6f1`, env `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`; plus `actions/cache` for `Library`. Public repos have free Actions minutes; private repos have a monthly free quota (amount not verified). Treat CI as optional/fragile for Personal licenses; fallback is manual local builds.

### Gaps
- Whether Unity 6.6 images exist in GameCI, current Personal activation status in 2026, Actions free-minute numbers.

## 9. Common errors

### Takeaway
Nearly all load-time errors are Content-Encoding / MIME mismatches.

### Cited Findings
- "Unable to parse Build/*.framework.js.br ... file is corrupt or compression was misconfigured": Brotli build served without matching Content-Encoding; fixes: set headers, or switch to Gzip/Disabled; debug via DevTools Network tab response headers — [Unity Discussions](https://discussions.unity.com/t/unity-export-webgl-error-unable-to-parse-build-webgame-framework-js-unityweb-the-file-is-corrupt-or-compression-was-misconfigured/305236), [itch.io thread](https://itch.io/post/6539624) (search summaries)
- Related forum thread on "HTTP response header Content-Type configured incorrectly for file Build/xxx.wasm.gz" — [Unity Discussions](https://discussions.unity.com/t/webgl-error-while-running-http-response-header-content-type-configured-incorrectly-on-the-server-for-file-build-xxx-wasm-gz/1633850) (title only)
- itch.io does not work with Brotli; Gzip works there — itch.io thread above.

### Inferences / UNVERIFIED
- Double compression (nginx `gzip on` on precompressed file, or a CDN recompressing) causes the same parse error. "Cannot compile wasm"/memory errors: raise Maximum Memory Size or reduce allocations; mobile Safari memory limits. Stale cache: Name Files As Hashes + no-cache on index.html, or clear IndexedDB (Data Caching).

### Gaps
- No source on memory-error specifics.

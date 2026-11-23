# CICAPOS AI Assist

Live **D.Nova OCR + Chat** for prefill, upload checks, and funding quality validation. Local demo catalog is **off** unless `?ai=demo`.

## Enable (required)

### A) Prototype / browser (fastest)

In the browser console (once):

```js
CitfAi.saveNovaConfig({
  apiKey: "ak-…",           // DeepSeek-V4-Flash
  ocrApiKey: "sk-…",        // OCR service
  model: "DeepSeek-V4-Flash",
  baseUrl: "https://nova.deloitte.com.cn/v1",
  ocrBaseUrl: "https://nova.deloitte.com.cn/nova-cloud/ocr/ocr/v1"
})
```

Or copy `portal/.../web-files/citf-nova.local.js.example` → `citf-nova.local.js` and fill keys (gitignored).

Then use One-click prefill / Upload / Save – it calls D.Nova directly (no demo).

### B) Dataverse plugin (Power Pages)

```bash
export NOVA_BASE_URL=https://nova.deloitte.com.cn/v1
export NOVA_API_KEY=ak-…
export NOVA_MODEL=DeepSeek-V4-Flash
export NOVA_OCR_API_KEY=sk-…
# then run CICAPOS.Register – syncs env cicapos_AiAssistConfig
```

| Capability | Keys | Flow |
|---|---|---|
| Submit / form quality | Chat `ak-` | Fields → DeepSeek → issues |
| One-click prefill / upload | OCR `sk-` + Chat `ak-` | File → OCR → Chat → fields |

## Modes

| Mode | When |
|---|---|
| **Live** (default) | `CITF_NOVA` keys or Custom API + Dataverse env |
| **Demo** | only `?ai=demo` or `CITF_AI_DEMO=true` |
| Unit tests | `AI_ALLOW_DEMO=true` |

## Contract

Custom API: `cicapos_AiAssist`  
Actions: `prefill` | `validate_upload` | `validate_form` | `suggest`  
Request: `fileName`, `fileBase64` (data URL ok)

## Code

| Area | Path |
|---|---|
| Config + OCR + clients | `plugins/CICAPOS.Plugins/Ai/` |
| Plugin | `plugins/CICAPOS.Plugins/AiAssistPlugin.cs` |
| Portal JS | `portal/.../web-files/citf-ai-assist.js` |

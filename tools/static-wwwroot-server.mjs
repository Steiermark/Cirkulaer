import http from "node:http";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(fileURLToPath(new URL("../src/App/wwwroot", import.meta.url)));
const port = Number(process.env.PORT || 4173);
const host = process.env.HOST || "0.0.0.0";
const apiBaseUrl = process.env.API_BASE_URL || "";
const apiKey = process.env.API_KEY || "";

const types = new Map([
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".svg", "image/svg+xml"],
  [".png", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".webp", "image/webp"],
  [".ico", "image/x-icon"],
]);

const server = http.createServer((request, response) => {
  const requestUrl = new URL(request.url || "/", `http://localhost:${port}`);
  let pathname = decodeURIComponent(requestUrl.pathname);
  if (pathname === "/") pathname = "/index.html";

  // App serves /config in the real stack; here it is stubbed so app.js takes its
  // same-origin fallback instead of hanging on a 404 body it cannot parse.
  if (pathname === "/config") {
    response.writeHead(200, {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
    });
    response.end(JSON.stringify({ apiBaseUrl: "", apiKey }));
    return;
  }

  if (pathname.startsWith("/api/")) {
    if (!apiBaseUrl) {
      response.writeHead(503, {
        "Content-Type": "application/json; charset=utf-8",
        "Cache-Control": "no-store",
      });
      response.end(JSON.stringify({
        error: "API'en kører ikke. Start hele .NET-stakken, eller start denne server med API_BASE_URL.",
      }));
      return;
    }

    proxyApiRequest(request, response, requestUrl).catch((error) => {
      response.writeHead(502, {
        "Content-Type": "application/json; charset=utf-8",
        "Cache-Control": "no-store",
      });
      response.end(JSON.stringify({ error: `API'en kunne ikke nås: ${error.message}` }));
    });
    return;
  }

  let filePath = path.resolve(root, `.${pathname}`);
  if (!filePath.startsWith(root)) {
    response.writeHead(403);
    response.end("Forbidden");
    return;
  }

  if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    filePath = path.join(root, "index.html");
  }

  response.writeHead(200, {
    "Content-Type": types.get(path.extname(filePath).toLowerCase()) || "application/octet-stream",
    "Cache-Control": "no-store",
  });
  fs.createReadStream(filePath).pipe(response);
});

function lanUrls() {
  return Object.values(os.networkInterfaces())
    .flat()
    .filter((entry) => entry && entry.family === "IPv4" && !entry.internal)
    .map((entry) => `http://${entry.address}:${port}`);
}

server.listen(port, host, () => {
  console.log(`Serving ${root}`);
  console.log(`  Local:   http://127.0.0.1:${port}`);
  for (const url of lanUrls()) {
    console.log(`  Mobile:  ${url}`);
  }
  if (apiBaseUrl) {
    console.log(`Proxying /api/* to ${apiBaseUrl}`);
  } else {
    console.log("Frontend only - /api/* returns a clear error. Run the AppHost for the full stack.");
  }
});

async function proxyApiRequest(request, response, requestUrl) {
  const target = new URL(`${requestUrl.pathname}${requestUrl.search}`, apiBaseUrl);
  const headers = { ...request.headers };
  headers.host = target.host;
  if (apiKey) headers["x-api-key"] = apiKey;

  const proxyResponse = await fetch(target, {
    method: request.method,
    headers,
    body: ["GET", "HEAD"].includes(request.method || "") ? undefined : request,
    duplex: "half",
  });

  response.writeHead(proxyResponse.status, {
    "Content-Type": proxyResponse.headers.get("content-type") || "application/json; charset=utf-8",
    "Cache-Control": "no-store",
  });
  response.end(Buffer.from(await proxyResponse.arrayBuffer()));
}

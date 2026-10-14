const http = require("node:http");
const os = require("node:os");

const DEFAULT_PORT = 34280;
const DEFAULT_HOST = "0.0.0.0";

function createLanQuotaServer({ getQuota }) {
  if (typeof getQuota !== "function") {
    throw new TypeError("createLanQuotaServer requires getQuota.");
  }

  const port = Number(process.env.CODEX_QUOTA_LAN_PORT || DEFAULT_PORT);
  const host = process.env.CODEX_QUOTA_LAN_HOST || DEFAULT_HOST;
  let server = null;

  function start() {
    if (server) return Promise.resolve(getStatus(host, port));

    server = http.createServer(async (request, response) => {
      response.setHeader("Access-Control-Allow-Origin", "*");
      response.setHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
      response.setHeader("Access-Control-Allow-Headers", "Content-Type");
      response.setHeader("Cache-Control", "no-store");

      if (request.method === "OPTIONS") {
        response.writeHead(204);
        response.end();
        return;
      }

      if (request.method !== "GET") {
        writeJson(response, 405, { ok: false, error: "method_not_allowed" });
        return;
      }

      const url = new URL(request.url || "/", `http://${request.headers.host || "localhost"}`);

      if (url.pathname === "/health") {
        writeJson(response, 200, {
          ok: true,
          service: "codex-quota-widget",
          port,
          urls: getLanUrls(port),
          servedAt: new Date().toISOString()
        });
        return;
      }

      if (url.pathname === "/" || url.pathname === "/quota") {
        try {
          const quota = await getQuota();
          writeJson(response, 200, {
            ok: true,
            ...quota,
            servedAt: new Date().toISOString()
          });
        } catch (error) {
          writeJson(response, 500, {
            ok: false,
            error: error.message || String(error),
            servedAt: new Date().toISOString()
          });
        }
        return;
      }

      writeJson(response, 404, { ok: false, error: "not_found" });
    });

    return new Promise((resolve) => {
      server.once("error", (error) => {
        console.warn(`[lan-quota] disabled: ${error.message}`);
        server = null;
        resolve(null);
      });

      server.listen(port, host, () => {
        const status = getStatus(host, port);
        console.log(`[lan-quota] listening on ${status.urls.join(", ")}`);
        resolve(status);
      });
    });
  }

  function stop() {
    return new Promise((resolve) => {
      if (!server) {
        resolve();
        return;
      }

      server.close(() => {
        server = null;
        resolve();
      });
    });
  }

  return {
    start,
    stop,
    getStatus: () => (server ? getStatus(host, port) : null)
  };
}

function writeJson(response, statusCode, payload) {
  const body = JSON.stringify(payload);
  response.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Content-Length": Buffer.byteLength(body)
  });
  response.end(body);
}

function getStatus(host, port) {
  return {
    host,
    port,
    urls: getLanUrls(port)
  };
}

function getLanUrls(port) {
  const urls = [`http://127.0.0.1:${port}/quota`];

  for (const info of Object.values(os.networkInterfaces()).flat()) {
    if (!info || info.family !== "IPv4" || info.internal) continue;
    urls.push(`http://${info.address}:${port}/quota`);
  }

  return urls;
}

module.exports = { createLanQuotaServer };

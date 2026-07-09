import http from "node:http";

const port = Number(process.argv[2] ?? 55910);

http.createServer((request, response) => {
  if (request.method === "GET" && request.url === "/health") {
    respond(response, { role: "Worker", status: "ok" });
    return;
  }

  if (request.method !== "POST" || request.url !== "/execute") {
    response.writeHead(404);
    response.end();
    return;
  }

  let body = "";
  request.on("data", chunk => body += chunk);
  request.on("end", () => {
    const isSynthesis = body.includes("Produce the final answer");
    respond(response, {
      content: isSynthesis ? "SYNTHESIZED_FINAL" : "WORKER_RESULT",
      model: "mock-company-model",
      provider: "mock",
      usage: { inputTokens: 5, outputTokens: 3 },
      isLocal: true
    });
  });
}).listen(port, "127.0.0.1");

function respond(response, value) {
  response.writeHead(200, { "content-type": "application/json" });
  response.end(JSON.stringify(value));
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("Method not allowed", {
        status: 405,
        headers: { Allow: "GET, HEAD" },
      });
    }

    if (!url.pathname.startsWith("/download/")) {
      return new Response("Not found", { status: 404 });
    }

    if (!env.DOWNLOAD_TOKEN) {
      return new Response("Download service is not configured.", {
        status: 503,
      });
    }

    const suppliedToken = url.searchParams.get("token");

    if (!suppliedToken || suppliedToken !== env.DOWNLOAD_TOKEN) {
      return new Response("Unauthorized", { status: 401 });
    }

    const objectKey = decodeURIComponent(
      url.pathname.substring("/download/".length)
    );

    if (!objectKey) {
      return new Response("File not specified", { status: 400 });
    }

    const object = await env.PREVIEW_BUILDS.get(objectKey);

    if (object === null) {
      return new Response("File not found", { status: 404 });
    }

    const headers = new Headers();

    object.writeHttpMetadata(headers);

    headers.set("etag", object.httpEtag);
    headers.set("Cache-Control", "private, no-store");

    const fileName =
      objectKey.split("/").pop() || "ADT-preview-download";

    headers.set(
      "Content-Disposition",
      `attachment; filename="${fileName.replace(/"/g, "")}"`
    );

    if (request.method === "HEAD") {
      return new Response(null, {
        status: 200,
        headers,
      });
    }

    return new Response(object.body, {
      status: 200,
      headers,
    });
  },
};
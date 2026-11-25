### Building and running your application

When you're ready, start your application by running:
`docker compose up --build`.

Your application will be available at http://localhost:8080.

### Deploying your application to the cloud

From the directory containing this README, run `docker compose build server`.
For a standalone image, run `docker build -f Dockerfile -t myapp ../../..`.
Add `--platform=linux/amd64` if your deployment host needs that architecture.
Run a standalone container with `docker run --rm -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 myapp`.

Then, push it to your registry, e.g. `docker push myregistry.com/myapp`.

Consult Docker's [getting started](https://docs.docker.com/go/get-started-sharing/)
docs for more detail on building and pushing.

### Share lifetime

Run a single application instance. Shares and nearby offers are held in memory, expire after one hour, and expire on restart. The app stores encrypted text under `App_Data/Snippets` while a share is active and cleans this directory on startup and every minute. Keep it writable by the application.

Nearby discovery starts when a device opens the main page. It groups visitors by public IPv4 address, private IPv4 /24 subnet, or IPv6 /64 subnet. If the app runs behind a reverse proxy, set `ShareAnywhere__TrustedProxies__0` to that proxy's IP address and have the proxy forward the client IP and scheme so discovery uses the visitor's address.

### Local regression tests

From the directory containing the outer solution, run `dotnet build` and then `node --test tests/share-smoke.test.mjs`.

### References
* [Docker's .NET guide](https://docs.docker.com/language/dotnet/)
* The [dotnet-docker](https://github.com/dotnet/dotnet-docker/tree/main/samples)
  repository has many relevant samples and docs.

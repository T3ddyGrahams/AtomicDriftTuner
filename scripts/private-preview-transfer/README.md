# Upload existing ADT preview packages privately

This helper transfers an already built installer and portable ZIP directly from a local PC to the private `adt-preview-builds` R2 bucket. GitHub Actions holds the long-lived R2 credentials. The application source and binaries are not uploaded to GitHub by this workflow.

## Configuration

The workflow uses the same repository configuration as Publish Preview:

- Secrets: `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `PREVIEW_PUBLISH_TOKEN`.
- Variables: `R2_ACCOUNT_ID`, `R2_BUCKET_NAME` (`adt-preview-builds`).
- Manager: `https://adt-preview-downloads.atomicdrifttuner.workers.dev`.

The admin `DOWNLOAD_TOKEN` is never read or changed. Run only from trusted `main`. The workflow shares the `adt-publish-preview` concurrency group with the existing publisher.

## Transfer

Install the locked dependencies with `npm ci --ignore-scripts --no-audit --no-fund`. Use Node 22 for Actions; the local helper supports Node 20 or later.

1. Put the existing `AtomicDriftTuner-<version>-setup.exe` and `AtomicDriftTuner-<version>-portable.zip` in a local package folder.
2. Create a new private local state folder with the prepare command below. Use a location outside anything to be committed or uploaded. On Windows, privacy relies on the parent folder's inherited NTFS permissions.
3. Submit the complete public `request.json` as `.github/adt-private-upload-request.json` in a reviewed commit to `main`; this automatically starts **Upload Local Preview Privately**. Alternatively, start it manually with the JSON as `request_json`. The request contains a public encryption key, release version, filenames, sizes, hashes and link lifetime. It contains no private key. Never commit any other state files.
4. Retrieve `grant.json` from the `adt-private-upload-<run-id>` artifact. Verify its exact repository, workflow, trusted main commit and run ID through GitHub before using it. Encryption authenticates the recipient, not the producer; do not accept a grant from an arbitrary attachment.
5. Run the upload command. It sends both local files directly to private R2 with overwrite protection and verifies the uploaded bytes. The running workflow independently verifies both files before preparing links.
6. After the workflow succeeds, run the links command. It saves the private announcement locally without printing signed URLs.

```text
node client.mjs prepare <version> <packages-dir> <new-state-dir> [hours]
node client.mjs upload <state-dir> <trusted-grant-file> <packages-dir>
node client.mjs links <state-dir> <trusted-grant-file>
```

Never commit or upload the state directory, private key or announcement. Keep state for recovery until the transfer succeeds, then remove the temporary private key when no longer needed.

## Privacy and recovery

Only the encrypted permission bundle is an Actions artifact. Packages, plaintext upload permissions and supporter links do not appear in public logs or artifacts. The public request file or manual workflow input contains release metadata and the temporary public encryption key. Only a change to the exact request path on `main` triggers automatic transfer; unrelated pushes and pull requests do not.

Permissions cover only the two exact package objects and the corresponding private announcement; they expire in 30 minutes. The workflow waits 20 minutes for uploads. Within an active grant, retries verify and reuse an already uploaded matching object. If permission expires after a partial upload, stop for reviewed recovery: new grants refuse existing objects. Never automatically delete or overwrite a published package.

Supporter links default to 24 hours, with a maximum seven-day lifetime. A recipient can forward a link; it is not individual membership authentication. The helper does not publish posts or change bucket visibility.

Run offline checks with `npm test`. Tests include actual SDK signing with fixture credentials, encrypted handoff, path and overwrite restrictions, changed packages, corrupt downloads and announcement validation. They make no live R2 requests.

See Cloudflare's [presigned URL documentation](https://developers.cloudflare.com/r2/api/s3/presigned-urls/) and [S3 API compatibility](https://developers.cloudflare.com/r2/api/s3/api/) for the underlying upload mechanism.

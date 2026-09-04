const { EmbedBuilder } = require("discord.js");

function trimText(value, max = 3000) {
  const text = String(value || "").trim();

  if (text.length <= max) {
    return text;
  }

  return `${text.slice(0, max - 1)}…`;
}

async function githubRequest(repo, path, token) {
  const response = await fetch(
    `https://api.github.com/repos/${repo}${path}`,
    {
      headers: {
        Accept: "application/vnd.github+json",
        "User-Agent": "Atomic-Drift-Tuner-Discord-Bot",
        ...(token
          ? { Authorization: `Bearer ${token}` }
          : {}),
      },
    }
  );

  if (!response.ok) {
    throw new Error(
      `GitHub API ${response.status}: ${await response.text()}`
    );
  }

  return response.json();
}

async function getDiscordChannel(
  client,
  guildId,
  channelId,
  channelName
) {
  if (channelId) {
    const found = await client.channels
      .fetch(channelId)
      .catch(() => null);

    if (found?.isTextBased()) {
      return found;
    }
  }

  const guild = await client.guilds.fetch(guildId);
  const channels = await guild.channels.fetch();

  const found = channels.find(
    (channel) =>
      channel?.isTextBased() &&
      channel.name === channelName
  );

  if (!found) {
    throw new Error(
      `Could not find Discord channel #${channelName}`
    );
  }

  return found;
}
function buildReleaseEmbed(release, repo) {
  return new EmbedBuilder()
    .setTitle(
      `🚀 ${release.name || release.tag_name}`
    )
    .setURL(release.html_url)
    .setDescription(
      trimText(
        release.body ||
          "No release notes were provided."
      )
    )
    .addFields(
      {
        name: "Version",
        value: release.tag_name || "Unknown",
        inline: true,
      },
      {
        name: "Published by",
        value: release.author?.login || "GitHub",
        inline: true,
      },
      {
        name: "Repository",
        value: repo,
        inline: true,
      }
    )
    .setFooter({
      text: "Atomic Drift Tuner • GitHub Release",
    })
    .setTimestamp(
      new Date(
        release.published_at ||
          release.created_at ||
          Date.now()
      )
    );
}

async function buildUpdateEmbed(
  event,
  repo,
  token
) {
  const actor = event.actor?.login || "GitHub";

  if (event.type === "PushEvent") {
    const branch = String(
      event.payload?.ref || ""
    ).replace("refs/heads/", "");

    const head = event.payload?.head;

    let message =
      `${event.payload?.size || 0} commit(s) pushed.`;

    if (head) {
      const commit = await githubRequest(
        repo,
        `/commits/${head}`,
        token
      ).catch(() => null);

      if (commit?.commit?.message) {
        message = trimText(
          commit.commit.message.split("\n")[0],
          700
        );
      }
    }

    return new EmbedBuilder()
      .setTitle(
        `🔧 Push to ${branch || "unknown"}`
      )
      .setURL(
        head
          ? `https://github.com/${repo}/commit/${head}`
          : `https://github.com/${repo}`
      )
      .setDescription(message)
      .addFields(
        {
          name: "Commits",
          value: String(
            event.payload?.size || 0
          ),
          inline: true,
        },
        {
          name: "By",
          value: actor,
          inline: true,
        }
      )
      .setFooter({
        text: "Atomic Drift Tuner • GitHub Update",
      })
      .setTimestamp(
        new Date(event.created_at)
      );
  }
  if (event.type === "PullRequestEvent") {
    const pr = event.payload?.pull_request;
    const action = event.payload?.action;

    if (
      !pr ||
      (
        action !== "opened" &&
        !(action === "closed" && pr.merged)
      )
    ) {
      return null;
    }

    const label =
      action === "opened"
        ? "Pull request opened"
        : "Pull request merged";

    return new EmbedBuilder()
      .setTitle(
        `🔀 ${label}: #${pr.number}`
      )
      .setURL(pr.html_url)
      .setDescription(
        trimText(pr.title, 1000)
      )
      .addFields({
        name: "By",
        value: actor,
        inline: true,
      })
      .setFooter({
        text: "Atomic Drift Tuner • GitHub Update",
      })
      .setTimestamp(
        new Date(event.created_at)
      );
  }

  if (event.type === "IssuesEvent") {
    const issue = event.payload?.issue;
    const action = event.payload?.action;

    if (
      !issue ||
      !["opened", "closed", "reopened"].includes(action)
    ) {
      return null;
    }

    return new EmbedBuilder()
      .setTitle(
        `🐛 Issue ${action}: #${issue.number}`
      )
      .setURL(issue.html_url)
      .setDescription(
        trimText(issue.title, 1000)
      )
      .addFields({
        name: "By",
        value: actor,
        inline: true,
      })
      .setFooter({
        text: "Atomic Drift Tuner • GitHub Update",
      })
      .setTimestamp(
        new Date(event.created_at)
      );
  }

  return null;
}

function buildCommitEmbed(commit, repo) {
  const sha = commit.sha || "";
  const message =
    commit.commit?.message?.split("\n")[0] ||
    "New commit pushed.";

  const author =
    commit.author?.login ||
    commit.commit?.author?.name ||
    "GitHub";

  return new EmbedBuilder()
    .setTitle(
      `🔧 New commit: ${sha.slice(0, 7)}`
    )
    .setURL(
      commit.html_url ||
        `https://github.com/${repo}/commit/${sha}`
    )
    .setDescription(
      trimText(message, 1000)
    )
    .addFields({
      name: "By",
      value: author,
      inline: true,
    })
    .setFooter({
      text: "Atomic Drift Tuner • GitHub Update",
    })
    .setTimestamp(
      new Date(
        commit.commit?.author?.date ||
          Date.now()
      )
    );
}

function startGitHubNotifier({
  client,
  guildId,
  repo,
  token,
  releasesChannelId,
  updatesChannelId,
  pollSeconds = 60,
}) {
  repo = String(repo || "")
    .trim()
    .replace(
      /^https?:\/\/github\.com\//i,
      ""
    )
    .replace(/\.git$/i, "")
    .replace(/\/$/, "");

  let firstRun = true;
  let lastEventId = null;
  let lastCommitSha = null;
  let busy = false;

  const seenReleases = new Set();

  async function poll() {
    if (busy) {
      return;
    }

    busy = true;

    try {
      const [releases, events, commits] =
        await Promise.all([
          githubRequest(
            repo,
            "/releases?per_page=10",
            token
          ),
          githubRequest(
            repo,
            "/events?per_page=100",
            token
          ),
          githubRequest(
            repo,
            "/commits?per_page=20",
            token
          ),
        ]);

      const publishedReleases =
        releases.filter(
          (release) => !release.draft
        );

      /*
       * On startup Atomic records what already
       * exists without posting old history.
       */
      if (firstRun) {
        for (
          const release of publishedReleases
        ) {
          seenReleases.add(
            String(release.id)
          );
        }

        lastEventId =
          events[0]?.id || null;

        lastCommitSha =
          commits[0]?.sha || null;

        firstRun = false;

        console.log(
          `GitHub notifier watching ${repo}`
        );

        return;
      }

      /*
       * NEW RELEASES
       */
      const newReleases =
        publishedReleases
          .filter(
            (release) =>
              !seenReleases.has(
                String(release.id)
              )
          )
          .reverse();

      if (newReleases.length > 0) {
        const releasesChannel =
          await getDiscordChannel(
            client,
            guildId,
            releasesChannelId,
            "releases"
          );

        for (
          const release of newReleases
        ) {
          await releasesChannel.send({
            embeds: [
              buildReleaseEmbed(
                release,
                repo
              ),
            ],
          });

          seenReleases.add(
            String(release.id)
          );
        }
      }
      /*
       * NEW COMMITS
       */
      const commitIndex =
        lastCommitSha
          ? commits.findIndex(
              (commit) =>
                commit.sha === lastCommitSha
            )
          : 0;

      const newCommits = (
        commitIndex < 0
          ? commits
          : commits.slice(
              0,
              commitIndex
            )
      ).reverse();

      if (newCommits.length > 0) {
        const updatesChannel =
          await getDiscordChannel(
            client,
            guildId,
            updatesChannelId,
            "github-updates"
          );

        for (const commit of newCommits) {
          await updatesChannel.send({
            embeds: [
              buildCommitEmbed(
                commit,
                repo
              ),
            ],
          });
        }
      }

      lastCommitSha =
        commits[0]?.sha ||
        lastCommitSha;

      /*
       * NEW GITHUB ACTIVITY
       */
      const previousIndex =
        lastEventId
          ? events.findIndex(
              (event) =>
                event.id === lastEventId
            )
          : 0;

      const newEvents = (
        previousIndex < 0
          ? events
          : events.slice(
              0,
              previousIndex
            )
      ).reverse();

      let updatesChannel = null;

      for (const event of newEvents) {
        /*
         * Releases are posted only
         * in #releases.
         */
        if (
          event.type === "ReleaseEvent" ||
          event.type === "PushEvent"
        ) {
          continue;
        }

        const embed =
          await buildUpdateEmbed(
            event,
            repo,
            token
          );

        if (!embed) {
          continue;
        }

        if (!updatesChannel) {
          updatesChannel =
            await getDiscordChannel(
              client,
              guildId,
              updatesChannelId,
              "github-updates"
            );
        }

        await updatesChannel.send({
          embeds: [embed],
        });
      }

      lastEventId =
        events[0]?.id ||
        lastEventId;
    } catch (error) {
      console.error(
        "GitHub notifier error:",
        error.message
      );
    } finally {
      busy = false;
    }
  }

  /*
   * Check once immediately, then
   * continue checking GitHub.
   */
  poll();

  setInterval(
    poll,
    Math.max(
      30,
      Number(pollSeconds) || 60
    ) * 1000
  );
}

module.exports = {
  startGitHubNotifier,
};

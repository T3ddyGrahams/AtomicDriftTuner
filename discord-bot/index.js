require("dotenv").config();

const {
  Client,
  GatewayIntentBits,
  REST,
  Routes,
  SlashCommandBuilder,
  EmbedBuilder,
  ModalBuilder,
  TextInputBuilder,
  TextInputStyle,
  ActionRowBuilder,
  Events,
  MessageFlags,
} = require("discord.js");

const TOKEN = process.env.DISCORD_TOKEN;
const CLIENT_ID = process.env.DISCORD_CLIENT_ID;
const GUILD_ID = process.env.DISCORD_GUILD_ID;
const GITHUB_REPO = process.env.GITHUB_REPO;
const GITHUB_TOKEN = process.env.GITHUB_TOKEN;

const BOT_VERSION = "0.2.0";

const requiredVariables = [
  TOKEN,
  CLIENT_ID,
  GUILD_ID,
  GITHUB_REPO,
];

if (requiredVariables.some((value) => !value)) {
  console.error("Missing required environment variables.");
  process.exit(1);
}

const client = new Client({
  intents: [GatewayIntentBits.Guilds],
});

const commands = [
  new SlashCommandBuilder()
    .setName("atomic")
    .setDescription("Atomic Drift Tuner commands")

    .addSubcommand((subcommand) =>
      subcommand
        .setName("help")
        .setDescription("Show available Atomic commands")
    )

    .addSubcommand((subcommand) =>
      subcommand
        .setName("latest")
        .setDescription("Show the latest Atomic Drift Tuner release")
    )

    .addSubcommand((subcommand) =>
      subcommand
        .setName("download")
        .setDescription("Get the latest Atomic Drift Tuner download")
    )

    .addSubcommand((subcommand) =>
      subcommand
        .setName("changelog")
        .setDescription("Show the latest Atomic Drift Tuner release notes")
    )

    .addSubcommand((subcommand) =>
      subcommand
        .setName("status")
        .setDescription("Show Atomic Bot status")
    )

    .addSubcommand((subcommand) =>
      subcommand
        .setName("bug")
        .setDescription("Submit an Atomic Drift Tuner bug report")
    )

    .addSubcommand((subcommand) =>
      subcommand
        .setName("feature")
        .setDescription("Submit an Atomic Drift Tuner feature request")
    ),
].map((command) => command.toJSON());

async function getLatestRelease() {
  const response = await fetch(
    `https://api.github.com/repos/${GITHUB_REPO}/releases?per_page=10`,
    {
      headers: {
        Accept: "application/vnd.github+json",
        "User-Agent": "Atomic-Drift-Tuner-Discord-Bot",
      },
    }
  );

  if (!response.ok) {
    throw new Error(`GitHub API returned ${response.status}`);
  }

  const releases = await response.json();

  const release = releases.find((item) => !item.draft);

  if (!release) {
    throw new Error("No published GitHub releases found.");
  }

  return release;
}

async function createGitHubIssue(title, body) {
  if (!GITHUB_TOKEN) {
    throw new Error("GitHub issue integration is not configured.");
  }

  const response = await fetch(
    `https://api.github.com/repos/${GITHUB_REPO}/issues`,
    {
      method: "POST",

      headers: {
        Accept: "application/vnd.github+json",
        Authorization: `Bearer ${GITHUB_TOKEN}`,
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "Atomic-Drift-Tuner-Discord-Bot",
        "Content-Type": "application/json",
      },

      body: JSON.stringify({
        title,
        body,
      }),
    }
  );

  if (!response.ok) {
    const errorText = await response.text();

    console.error(
      `GitHub issue creation failed: ${response.status} ${errorText}`
    );

    throw new Error(`GitHub API returned ${response.status}`);
  }

  return response.json();
}

async function registerCommands() {
  const rest = new REST({
    version: "10",
  }).setToken(TOKEN);

  console.log("Registering Atomic slash commands...");

  await rest.put(
    Routes.applicationGuildCommands(CLIENT_ID, GUILD_ID),
    {
      body: commands,
    }
  );

  console.log("Atomic slash commands registered.");
}

function formatUptime(seconds) {
  const days = Math.floor(seconds / 86400);

  const hours = Math.floor(
    (seconds % 86400) / 3600
  );

  const minutes = Math.floor(
    (seconds % 3600) / 60
  );

  return `${days}d ${hours}h ${minutes}m`;
}

function buildBugModal() {
  const modal = new ModalBuilder()
    .setCustomId("atomic_bug_modal")
    .setTitle("Atomic Drift Tuner Bug Report");

  const title = new TextInputBuilder()
    .setCustomId("bug_title")
    .setLabel("Short description")
    .setStyle(TextInputStyle.Short)
    .setPlaceholder("Example: Car detection misses CSP cars")
    .setRequired(true)
    .setMaxLength(100);

  const description = new TextInputBuilder()
    .setCustomId("bug_description")
    .setLabel("What happened?")
    .setStyle(TextInputStyle.Paragraph)
    .setPlaceholder(
      "Tell us what went wrong and what you expected to happen."
    )
    .setRequired(true)
    .setMaxLength(1500);

  const steps = new TextInputBuilder()
    .setCustomId("bug_steps")
    .setLabel("How can we reproduce it?")
    .setStyle(TextInputStyle.Paragraph)
    .setPlaceholder(
      "List the steps that cause the problem."
    )
    .setRequired(false)
    .setMaxLength(1500);

  const version = new TextInputBuilder()
    .setCustomId("bug_version")
    .setLabel("Atomic Drift Tuner version")
    .setStyle(TextInputStyle.Short)
    .setPlaceholder("Example: v0.8.0-beta")
    .setRequired(false)
    .setMaxLength(50);

  modal.addComponents(
    new ActionRowBuilder().addComponents(title),
    new ActionRowBuilder().addComponents(description),
    new ActionRowBuilder().addComponents(steps),
    new ActionRowBuilder().addComponents(version)
  );

  return modal;
}

function buildFeatureModal() {
  const modal = new ModalBuilder()
    .setCustomId("atomic_feature_modal")
    .setTitle("Atomic Drift Tuner Feature Request");

  const title = new TextInputBuilder()
    .setCustomId("feature_title")
    .setLabel("Feature name")
    .setStyle(TextInputStyle.Short)
    .setPlaceholder("Example: Per-car drift profiles")
    .setRequired(true)
    .setMaxLength(100);

  const description = new TextInputBuilder()
    .setCustomId("feature_description")
    .setLabel("What should Atomic do?")
    .setStyle(TextInputStyle.Paragraph)
    .setPlaceholder(
      "Describe the feature you'd like to see."
    )
    .setRequired(true)
    .setMaxLength(1500);

  const benefit = new TextInputBuilder()
    .setCustomId("feature_benefit")
    .setLabel("Why would this be useful?")
    .setStyle(TextInputStyle.Paragraph)
    .setPlaceholder(
      "Explain how this would improve Atomic Drift Tuner."
    )
    .setRequired(false)
    .setMaxLength(1000);

  modal.addComponents(
    new ActionRowBuilder().addComponents(title),
    new ActionRowBuilder().addComponents(description),
    new ActionRowBuilder().addComponents(benefit)
  );

  return modal;
}

client.once(Events.ClientReady, (readyClient) => {
  console.log(
    `Atomic Bot online as ${readyClient.user.tag}`
  );
});

client.on(Events.InteractionCreate, async (interaction) => {
  try {
    if (interaction.isChatInputCommand()) {
      if (interaction.commandName !== "atomic") {
        return;
      }

      const subcommand =
        interaction.options.getSubcommand();

      if (subcommand === "help") {
        const embed = new EmbedBuilder()
          .setTitle("⚛️ Atomic Drift Tuner")
          .setDescription(`Atomic Bot v${BOT_VERSION}`)
          .addFields(
            {
              name: "/atomic latest",
              value: "Show the newest published release.",
            },
            {
              name: "/atomic download",
              value: "Get the newest official GitHub release.",
            },
            {
              name: "/atomic changelog",
              value: "Show the newest release notes.",
            },
            {
              name: "/atomic status",
              value: "Show Atomic Bot status.",
            },
            {
              name: "/atomic bug",
              value: "Submit a bug report.",
            },
            {
              name: "/atomic feature",
              value: "Submit a feature request.",
            },
            {
              name: "/atomic help",
              value: "Show this command list.",
            }
          )
          .setFooter({
            text: "Atomic Drift Tuner",
          });

        await interaction.reply({
          embeds: [embed],
        });

        return;
      }

      if (subcommand === "bug") {
        await interaction.showModal(
          buildBugModal()
        );

        return;
      }

      if (subcommand === "feature") {
        await interaction.showModal(
          buildFeatureModal()
        );

        return;
      }

      if (subcommand === "status") {
        const uptime = formatUptime(
          process.uptime()
        );

        const latency = Math.round(
          client.ws.ping
        );

        const embed = new EmbedBuilder()
          .setTitle("⚛️ Atomic Bot Status")
          .setDescription(
            "Atomic Drift Tuner services are online."
          )
          .addFields(
            {
              name: "Bot Version",
              value: `v${BOT_VERSION}`,
              inline: true,
            },
            {
              name: "Status",
              value: "🟢 Online",
              inline: true,
            },
            {
              name: "Discord Latency",
              value: `${latency} ms`,
              inline: true,
            },
            {
              name: "Uptime",
              value: uptime,
              inline: true,
            },
            {
              name: "Repository",
              value: GITHUB_REPO,
              inline: true,
            }
          )
          .setFooter({
            text: "Atomic Drift Tuner",
          });

        await interaction.reply({
          embeds: [embed],
        });

        return;
      }

      await interaction.deferReply();

      const release =
        await getLatestRelease();

      if (subcommand === "latest") {
        const embed = new EmbedBuilder()
          .setTitle(
            `⚛️ ${
              release.name ||
              release.tag_name
            }`
          )
          .setURL(release.html_url)
          .setDescription(
            release.prerelease
              ? "Latest Atomic Drift Tuner prerelease."
              : "Latest Atomic Drift Tuner release."
          )
          .addFields(
            {
              name: "Version",
              value: release.tag_name,
              inline: true,
            },
            {
              name: "Type",
              value: release.prerelease
                ? "Beta / Prerelease"
                : "Stable",
              inline: true,
            },
            {
              name: "Published",
              value: new Date(
                release.published_at
              ).toLocaleDateString(),
              inline: true,
            }
          )
          .setFooter({
            text: "Atomic Drift Tuner • GitHub",
          });

        await interaction.editReply({
          embeds: [embed],
        });

        return;
      }

      if (subcommand === "download") {
        const embed = new EmbedBuilder()
          .setTitle(
            "⬇️ Download Atomic Drift Tuner"
          )
          .setURL(release.html_url)
          .setDescription(
            `Newest published release: **${release.tag_name}**\n\nOpen the GitHub release page to download the official files.`
          );

        await interaction.editReply({
          embeds: [embed],
        });

        return;
      }

      if (subcommand === "changelog") {
        let body =
          release.body ||
          "No release notes were provided.";

        if (body.length > 3500) {
          body =
            body.slice(0, 3500) +
            "\n\n…View the GitHub release for the full changelog.";
        }

        const embed = new EmbedBuilder()
          .setTitle(
            `📋 ${
              release.name ||
              release.tag_name
            }`
          )
          .setURL(release.html_url)
          .setDescription(body)
          .setFooter({
            text: "Atomic Drift Tuner • Changelog",
          });

        await interaction.editReply({
          embeds: [embed],
        });
      }

      return;
    }

    if (
      interaction.isModalSubmit() &&
      interaction.customId ===
        "atomic_bug_modal"
    ) {
      await interaction.deferReply({
        flags: MessageFlags.Ephemeral,
      });

      const title =
        interaction.fields.getTextInputValue(
          "bug_title"
        );

      const description =
        interaction.fields.getTextInputValue(
          "bug_description"
        );

      const steps =
        interaction.fields.getTextInputValue(
          "bug_steps"
        ) || "Not provided.";

      const version =
        interaction.fields.getTextInputValue(
          "bug_version"
        ) || "Not provided.";

      const issueBody = [
        "## Bug Report",
        "",
        description,
        "",
        "## Steps to Reproduce",
        "",
        steps,
        "",
        "## Atomic Drift Tuner Version",
        "",
        version,
        "",
        "## Submitted From",
        "",
        `Discord user: ${interaction.user.username}`,
        `Discord user ID: ${interaction.user.id}`,
        "",
        "---",
        "",
        `Submitted automatically by Atomic Bot v${BOT_VERSION}.`,
      ].join("\n");

      const issue =
        await createGitHubIssue(
          `[Bug] ${title}`,
          issueBody
        );

      const embed = new EmbedBuilder()
        .setTitle(
          "🐛 Bug Report Submitted"
        )
        .setDescription(
          "Your report has been added to the Atomic Drift Tuner GitHub issue tracker."
        )
        .addFields({
          name: `Issue #${issue.number}`,
          value: issue.html_url,
        })
        .setURL(issue.html_url)
        .setFooter({
          text: "Atomic Drift Tuner",
        });

      await interaction.editReply({
        embeds: [embed],
      });

      return;
    }

    if (
      interaction.isModalSubmit() &&
      interaction.customId ===
        "atomic_feature_modal"
    ) {
      await interaction.deferReply({
        flags: MessageFlags.Ephemeral,
      });

      const title =
        interaction.fields.getTextInputValue(
          "feature_title"
        );

      const description =
        interaction.fields.getTextInputValue(
          "feature_description"
        );

      const benefit =
        interaction.fields.getTextInputValue(
          "feature_benefit"
        ) || "Not provided.";

      const issueBody = [
        "## Feature Request",
        "",
        description,
        "",
        "## Why This Would Be Useful",
        "",
        benefit,
        "",
        "## Submitted From",
        "",
        `Discord user: ${interaction.user.username}`,
        `Discord user ID: ${interaction.user.id}`,
        "",
        "---",
        "",
        `Submitted automatically by Atomic Bot v${BOT_VERSION}.`,
      ].join("\n");

      const issue =
        await createGitHubIssue(
          `[Feature] ${title}`,
          issueBody
        );

      const embed = new EmbedBuilder()
        .setTitle(
          "💡 Feature Request Submitted"
        )
        .setDescription(
          "Your idea has been added to the Atomic Drift Tuner GitHub issue tracker."
        )
        .addFields({
          name: `Issue #${issue.number}`,
          value: issue.html_url,
        })
        .setURL(issue.html_url)
        .setFooter({
          text: "Atomic Drift Tuner",
        });

      await interaction.editReply({
        embeds: [embed],
      });

      return;
    }
  } catch (error) {
    console.error(error);

    const message =
      "Atomic couldn't complete that request right now.";

    if (
      interaction.deferred ||
      interaction.replied
    ) {
      await interaction.editReply({
        content: message,
        embeds: [],
      });
    } else {
      await interaction.reply({
        content: message,
        flags: MessageFlags.Ephemeral,
      });
    }
  }
});

registerCommands()
  .then(() => client.login(TOKEN))
  .catch((error) => {
    console.error(error);
    process.exit(1);
  });

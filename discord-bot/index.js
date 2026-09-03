require("dotenv").config();

const {
  Client,
  GatewayIntentBits,
  REST,
  Routes,
  SlashCommandBuilder,
  EmbedBuilder,
} = require("discord.js");

const TOKEN = process.env.DISCORD_TOKEN;
const CLIENT_ID = process.env.DISCORD_CLIENT_ID;
const GUILD_ID = process.env.DISCORD_GUILD_ID;
const GITHUB_REPO = process.env.GITHUB_REPO;

if ([TOKEN, CLIENT_ID, GUILD_ID, GITHUB_REPO].some((value) => !value)) {
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
    ),
].map((command) => command.toJSON());

async function getLatestRelease() {
  const response = await fetch(
    `https://api.github.com/repos/${GITHUB_REPO}/releases/latest`,
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

  return response.json();
}

async function registerCommands() {
  const rest = new REST({ version: "10" }).setToken(TOKEN);

  console.log("Registering Atomic slash commands...");

  await rest.put(
    Routes.applicationGuildCommands(CLIENT_ID, GUILD_ID),
    { body: commands }
  );

  console.log("Atomic slash commands registered.");
}

client.once("ready", () => {
  console.log(`Atomic Bot online as ${client.user.tag}`);
});

client.on("interactionCreate", async (interaction) => {
  if (!interaction.isChatInputCommand()) return;
  if (interaction.commandName !== "atomic") return;

  const subcommand = interaction.options.getSubcommand();

  try {
    if (subcommand === "help") {
      const embed = new EmbedBuilder()
        .setTitle("⚛️ Atomic Drift Tuner")
        .setDescription("Atomic Bot v0.1")
        .addFields(
          { name: "/atomic latest", value: "Show the latest stable release." },
          { name: "/atomic download", value: "Get the latest official GitHub release." },
          { name: "/atomic changelog", value: "Show the latest release notes." },
          { name: "/atomic help", value: "Show this command list." }
        )
        .setFooter({ text: "Atomic Drift Tuner" });

      await interaction.reply({ embeds: [embed] });
      return;
    }

    await interaction.deferReply();

    const release = await getLatestRelease();

    if (subcommand === "latest") {
      const embed = new EmbedBuilder()
        .setTitle(`⚛️ ${release.name || release.tag_name}`)
        .setURL(release.html_url)
        .setDescription("Latest stable Atomic Drift Tuner release.")
        .addFields(
          { name: "Version", value: release.tag_name, inline: true },
          {
            name: "Published",
            value: new Date(release.published_at).toLocaleDateString(),
            inline: true,
          }
        )
        .setFooter({ text: "Atomic Drift Tuner • GitHub" });

      await interaction.editReply({ embeds: [embed] });
      return;
    }

    if (subcommand === "download") {
      const embed = new EmbedBuilder()
        .setTitle("⬇️ Download Atomic Drift Tuner")
        .setURL(release.html_url)
        .setDescription(
          `Latest stable release: **${release.tag_name}**\n\nOpen the GitHub release page to download the official files.`
        );

      await interaction.editReply({ embeds: [embed] });
      return;
    }

    if (subcommand === "changelog") {
      let body = release.body || "No release notes were provided.";

      if (body.length > 3500) {
        body =
          body.slice(0, 3500) +
          "\n\n…View the GitHub release for the full changelog.";
      }

      const embed = new EmbedBuilder()
        .setTitle(`📋 ${release.name || release.tag_name}`)
        .setURL(release.html_url)
        .setDescription(body)
        .setFooter({ text: "Atomic Drift Tuner • Changelog" });

      await interaction.editReply({ embeds: [embed] });
    }
  } catch (error) {
    console.error(error);

    const message =
      "Atomic couldn't retrieve the requested information right now.";

    if (interaction.deferred || interaction.replied) {
      await interaction.editReply(message);
    } else {
      await interaction.reply({
        content: message,
        ephemeral: true,
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

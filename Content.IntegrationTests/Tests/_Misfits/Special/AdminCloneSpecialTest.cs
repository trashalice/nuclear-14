using Content.Client.Lobby;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Client.State;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._Misfits.Special;

[TestFixture]
[TestOf(typeof(SharedSpecialSystem))]
public sealed class AdminCloneSpecialTest
{
    [Test]
    public async Task AdminCloneBodyUsesSelectedProfileSpecial()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;

        var clientNetManager = client.ResolveDependency<IClientNetManager>();
        var clientStateManager = client.ResolveDependency<IStateManager>();
        var clientPrefManager = client.ResolveDependency<IClientPreferencesManager>();
        var serverPrefManager = server.ResolveDependency<IServerPreferencesManager>();
        var playerManager = server.ResolveDependency<IPlayerManager>();
        var map = await pair.CreateTestMap();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => clientStateManager.CurrentState is LobbyState, 600);

        var userId = clientNetManager.ServerChannel!.UserId;
        var selectedSpecial = new SpecialProfile
        {
            Strength = 1,
            Perception = 2,
            Endurance = 3,
            Charisma = 4,
            Intelligence = 6,
            Agility = 10,
            Luck = 9,
        };

        HumanoidCharacterProfile selectedProfile = default!;

        await client.WaitAssertion(() =>
        {
            selectedProfile = HumanoidCharacterProfile.DefaultWithSpecies()
                .WithName("Selected Special")
                .WithSpecial(selectedSpecial);

            clientPrefManager.CreateCharacter(selectedProfile);
        });

        await PoolManager.WaitUntil(
            server,
            () => serverPrefManager.GetPreferences(userId).Characters.Count == 2,
            maxTicks: 60);

        await client.WaitAssertion(() => clientPrefManager.SelectCharacter(1));

        await PoolManager.WaitUntil(
            server,
            () => serverPrefManager.GetPreferences(userId).SelectedCharacterIndex == 1,
            maxTicks: 60);

        await server.WaitAssertion(() =>
        {
            var ticker = server.EntMan.System<GameTicker>();
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var special = server.EntMan.System<SharedSpecialSystem>();
            var session = playerManager.GetSessionById(userId);

            var oldBody = spawning.SpawnPlayerMob(
                map.GridCoords,
                null,
                HumanoidCharacterProfile.DefaultWithSpecies().WithName("Previous Body"),
                null);

            Assert.That(special.TryModifyTemporary(oldBody, SpecialStat.Strength, 3, source: "clone-test"), Is.True);
            Assert.That(server.EntMan.GetComponent<SpecialComponent>(oldBody).TemporaryStrengthModifier, Is.EqualTo(3));

            var profile = ticker.GetPlayerProfile(session);
            Assert.That(profile.MemberwiseEquals(selectedProfile), Is.True);

            var newBody = spawning.SpawnPlayerMob(map.GridCoords, null, profile, null);

            Assert.That(server.EntMan.TryGetComponent<SpecialComponent>(newBody, out var newSpecial), Is.True);
            Assert.That(newSpecial!.BaseStrength, Is.EqualTo(selectedSpecial.Strength));
            Assert.That(newSpecial.BasePerception, Is.EqualTo(selectedSpecial.Perception));
            Assert.That(newSpecial.BaseEndurance, Is.EqualTo(selectedSpecial.Endurance));
            Assert.That(newSpecial.BaseCharisma, Is.EqualTo(selectedSpecial.Charisma));
            Assert.That(newSpecial.BaseIntelligence, Is.EqualTo(selectedSpecial.Intelligence));
            Assert.That(newSpecial.BaseAgility, Is.EqualTo(selectedSpecial.Agility));
            Assert.That(newSpecial.BaseLuck, Is.EqualTo(selectedSpecial.Luck));
            Assert.That(newSpecial.TemporaryStrengthModifier, Is.Zero);
            Assert.That(newSpecial.TemporaryPerceptionModifier, Is.Zero);
            Assert.That(newSpecial.TemporaryEnduranceModifier, Is.Zero);
            Assert.That(newSpecial.TemporaryCharismaModifier, Is.Zero);
            Assert.That(newSpecial.TemporaryIntelligenceModifier, Is.Zero);
            Assert.That(newSpecial.TemporaryAgilityModifier, Is.Zero);
            Assert.That(newSpecial.TemporaryLuckModifier, Is.Zero);
        });

        await pair.CleanReturnAsync();
    }
}

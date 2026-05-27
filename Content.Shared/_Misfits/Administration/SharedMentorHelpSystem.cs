// #Misfits Change - Shared mentor help system and message types
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Administration;

/// <summary>
/// Shared base system for the mentor help (MHelp) messaging system.
/// Similar to SharedBwoinkSystem but for mentor-player communication.
/// </summary>
public abstract class SharedMentorHelpSystem : EntitySystem
{
    public static NetUserId SystemUserId { get; } = new NetUserId(Guid.Empty);

    public override void Initialize()
    {
        base.Initialize();
        // Mentor help has been retired; leave the shared message hook disabled.
        return;
    }

    protected virtual void OnMentorHelpTextMessage(MentorHelpTextMessage message, EntitySessionEventArgs eventArgs)
    {
    }

    [Serializable, NetSerializable]
    public sealed class MentorHelpTextMessage : EntityEventArgs
    {
        public DateTime SentAt { get; }
        public NetUserId UserId { get; }
        public NetUserId TrueSender { get; }
        public string Text { get; }
        public bool PlaySound { get; }

        public MentorHelpTextMessage(NetUserId userId, NetUserId trueSender, string text, DateTime? sentAt = default, bool playSound = true)
        {
            SentAt = sentAt ?? DateTime.Now;
            UserId = userId;
            TrueSender = trueSender;
            Text = text;
            PlaySound = playSound;
        }
    }
}

/// <summary>
/// Sent by the client to notify the server when it begins or stops typing in mentor help.
/// </summary>
[Serializable, NetSerializable]
public sealed class MentorHelpClientTypingUpdated : EntityEventArgs
{
    public NetUserId Channel { get; }
    public bool Typing { get; }

    public MentorHelpClientTypingUpdated(NetUserId channel, bool typing)
    {
        Channel = channel;
        Typing = typing;
    }
}

/// <summary>
/// Sent by server to notify mentors when a player begins or stops typing.
/// </summary>
[Serializable, NetSerializable]
public sealed class MentorHelpPlayerTypingUpdated : EntityEventArgs
{
    public NetUserId Channel { get; }
    public string PlayerName { get; }
    public bool Typing { get; }

    public MentorHelpPlayerTypingUpdated(NetUserId channel, string playerName, bool typing)
    {
        Channel = channel;
        PlayerName = playerName;
        Typing = typing;
    }
}

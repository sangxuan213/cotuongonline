using System.Media;

namespace UDM18.Client.Services;

public sealed class GameAudioService
{
    private readonly Dictionary<string, SoundPlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    public bool Enabled { get; set; } = true;

    public void PlayMove(string pieceId, bool isCapture, bool isCheck)
    {
        if (!CanPlay) return;
        if (isCheck) { PlayResource("Chieu.wav", SystemSounds.Hand); return; }
        if (!isCapture) { PlayResource("Mark.wav", SystemSounds.Beep); return; }
        var sound = pieceId.ToUpperInvariant() switch
        {
            var id when id.Contains("CHARIOT") => "XeAn.wav",
            var id when id.Contains("HORSE") => "MaAn.wav",
            var id when id.Contains("CANNON") => "PhaoAn.wav",
            var id when id.Contains("ADVISOR") => "SyAn.wav",
            var id when id.Contains("ELEPHANT") => "TinhAn.wav",
            var id when id.Contains("GENERAL") => "TuongAn.wav",
            _ => "ChotAn.wav"
        };
        PlayResource(sound, SystemSounds.Exclamation);
    }

    public void PlayGameEnded()
    {
        if (CanPlay) SystemSounds.Asterisk.Play();
    }

    public void PlayNotification()
    {
        if (CanPlay) PlayResource("Ready.wav", SystemSounds.Question);
    }

    public void PlayRejected()
    {
        if (CanPlay) SystemSounds.Hand.Play();
    }

    private bool CanPlay => Enabled && System.Windows.Application.Current is not null;

    private void PlayResource(string fileName, SystemSound fallback)
    {
        try
        {
            if (!_players.TryGetValue(fileName, out var player))
            {
                var resource = System.Windows.Application.GetResourceStream(
                    new Uri($"pack://application:,,,/Assets/Classic/Sounds/{fileName}", UriKind.Absolute));
                if (resource?.Stream is null) { fallback.Play(); return; }
                player = new SoundPlayer(resource.Stream);
                player.Load();
                _players[fileName] = player;
            }
            player.Play();
        }
        catch { fallback.Play(); }
    }
}

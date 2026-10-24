using NAudio.CoreAudioApi;

namespace MeetingNotesMcp.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, DataFlow Flow, bool IsDefault);

public static class AudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> List()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaults = new HashSet<string>();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            try
            {
                using var d = enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Console);
                defaults.Add(d.ID);
            }
            catch
            {
                // sem dispositivo padrão desse tipo
            }
        }

        var list = new List<AudioDeviceInfo>();
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
        {
            using (d) list.Add(new AudioDeviceInfo(d.ID, d.FriendlyName, d.DataFlow, defaults.Contains(d.ID)));
        }
        return list;
    }

    /// <summary>Resolve um dispositivo pelo trecho do nome, ou o padrão do sistema quando o filtro é vazio.</summary>
    public static MMDevice? Resolve(DataFlow flow, string? nameFilter)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            foreach (var d in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                if (d.FriendlyName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) return d;
                d.Dispose();
            }
            return null;
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, flow == DataFlow.Capture ? Role.Communications : Role.Console);
        }
        catch
        {
            return null;
        }
    }
}

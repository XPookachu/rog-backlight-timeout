using Microsoft.Extensions.Time.Testing;

namespace RogBacklightTimeout.Tests;

public sealed class IdleControllerTests
{
    private sealed class FakeBacklight : IBacklight
    {
        public List<int> Writes { get; } = [];
        public bool Fail { get; set; }

        public void SetLevel(int level)
        {
            if (Fail) throw new InvalidOperationException("boom");
            Writes.Add(level);
        }
    }

    private sealed class FakeLevels(int level) : ILevelSource
    {
        public int Level { get; set; } = level;
        public long Stamp { get; set; }
        public bool Throw { get; set; }

        public int GetRestoreLevel() => Throw ? throw new InvalidOperationException("bad config") : Level;
    }

    private sealed class NullLog : ILog
    {
        public void Info(string message) { }
    }

    private sealed class Harness : IDisposable
    {
        public FakeTimeProvider Time { get; } = new();
        public FakeBacklight Backlight { get; } = new();
        public FakeLevels Levels { get; }
        public Settings Settings { get; set; }
        public IdleController Controller { get; }

        public Harness(int level = 2, int timeoutSeconds = 30, bool enabled = true)
        {
            Levels = new FakeLevels(level);
            Settings = new Settings { Enabled = enabled, TimeoutSeconds = timeoutSeconds };
            Controller = new IdleController(Backlight, Levels, Time, () => Settings, new NullLog());
        }

        public void Dispose() => Controller.Dispose();
    }

    [Fact]
    public void Dims_after_timeout_without_internal_input()
    {
        using var h = new Harness(timeoutSeconds: 30);

        h.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(h.Backlight.Writes);

        h.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal([0], h.Backlight.Writes);
        Assert.True(h.Controller.IsDimmed);
    }

    [Fact]
    public void Internal_input_postpones_dimming()
    {
        using var h = new Harness(timeoutSeconds: 30);

        h.Time.Advance(TimeSpan.FromSeconds(20));
        h.Controller.OnInternalInput();
        h.Time.Advance(TimeSpan.FromSeconds(20));
        Assert.Empty(h.Backlight.Writes);

        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal([0], h.Backlight.Writes);
    }

    [Fact]
    public void Internal_input_restores_the_ghelper_level_and_rearms()
    {
        using var h = new Harness(level: 3, timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));

        h.Levels.Level = 2; // user changed brightness in G-Helper while dimmed
        h.Controller.OnInternalInput();
        Assert.Equal([0, 2], h.Backlight.Writes);
        Assert.False(h.Controller.IsDimmed);

        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal([0, 2, 0], h.Backlight.Writes);
    }

    [Fact]
    public void Does_nothing_when_backlight_is_already_off_in_ghelper()
    {
        using var h = new Harness(level: 0);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        h.Controller.OnInternalInput();

        Assert.Empty(h.Backlight.Writes);
    }

    [Fact]
    public void Disabled_never_dims_and_disabling_restores()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.True(h.Controller.IsDimmed);

        h.Settings = h.Settings with { Enabled = false };
        h.Controller.OnSettingsChanged();
        Assert.Equal([0, 2], h.Backlight.Writes);

        h.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal([0, 2], h.Backlight.Writes);
    }

    [Fact]
    public void Resume_and_power_events_rearm_without_writing()
    {
        using var h = new Harness(timeoutSeconds: 30);

        h.Time.Advance(TimeSpan.FromSeconds(25));
        h.Controller.OnSystemEvent("resume");
        h.Time.Advance(TimeSpan.FromSeconds(25));
        Assert.Empty(h.Backlight.Writes);

        h.Time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal([0], h.Backlight.Writes);
    }

    [Fact]
    public void Lid_closed_blocks_dimming_and_restoring()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Controller.OnLid(closed: true);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Controller.OnInternalInput();
        Assert.Empty(h.Backlight.Writes);

        h.Controller.OnLid(closed: false);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal([0], h.Backlight.Writes);
    }

    [Fact]
    public void Session_disconnect_restores_a_dimmed_keyboard_and_pauses_dimming()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));

        h.Controller.OnSessionDisconnected(true);
        Assert.Equal([0, 2], h.Backlight.Writes);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal([0, 2], h.Backlight.Writes);

        h.Controller.OnSessionDisconnected(false);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal([0, 2, 0], h.Backlight.Writes);
    }

    [Fact]
    public void Failed_restore_is_retried_on_next_input()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));

        h.Backlight.Fail = true;
        h.Controller.OnInternalInput();
        Assert.True(h.Controller.IsDimmed);

        h.Backlight.Fail = false;
        h.Controller.OnInternalInput();
        Assert.False(h.Controller.IsDimmed);
        Assert.Equal([0, 2], h.Backlight.Writes);
    }

    [Fact]
    public void Shutdown_never_leaves_the_keyboard_dark()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));

        h.Controller.Shutdown();

        Assert.Equal([0, 2], h.Backlight.Writes);
    }

    [Fact]
    public void Backlight_turned_on_later_is_picked_up_without_internal_input()
    {
        using var h = new Harness(level: 0, timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(25));
        Assert.Empty(h.Backlight.Writes);

        h.Levels.Level = 1; // user switched the backlight on in G-Helper
        h.Time.Advance(TimeSpan.FromSeconds(11));

        Assert.Equal([0], h.Backlight.Writes);
        Assert.True(h.Controller.IsDimmed);
    }

    [Fact]
    public void After_resume_the_light_is_dimmed_again_even_if_ghelper_relit_it()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal([0], h.Backlight.Writes);

        h.Controller.OnSystemEvent("resume"); // G-Helper may have re-applied brightness: state unknown
        h.Time.Advance(TimeSpan.FromSeconds(11));

        Assert.Equal([0, 0], h.Backlight.Writes);
        Assert.True(h.Controller.IsDimmed);
    }

    [Fact]
    public void Input_after_resume_restores_in_case_the_light_stayed_dark()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));

        h.Controller.OnSystemEvent("resume");
        h.Controller.OnInternalInput();

        Assert.Equal([0, 2], h.Backlight.Writes);
    }

    [Fact]
    public void Ghelper_settings_change_while_dimmed_resets_to_unknown_and_dims_again()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.True(h.Controller.IsDimmed);

        h.Levels.Stamp++; // e.g. Fn brightness keys changed G-Helper's config
        h.Time.Advance(TimeSpan.FromSeconds(10)); // polling notices the change
        Assert.False(h.Controller.IsDimmed);

        h.Time.Advance(TimeSpan.FromSeconds(11)); // idle again: dims again
        Assert.Equal([0, 0], h.Backlight.Writes);
        Assert.True(h.Controller.IsDimmed);
    }

    [Fact]
    public void Nothing_is_written_after_shutdown()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        h.Controller.Shutdown();

        h.Time.Advance(TimeSpan.FromMinutes(5));
        h.Controller.OnInternalInput();

        Assert.Empty(h.Backlight.Writes);
    }

    [Fact]
    public void Unreadable_ghelper_config_falls_back_instead_of_crashing()
    {
        using var h = new Harness(level: 2, timeoutSeconds: 10);
        h.Levels.Throw = true;

        h.Time.Advance(TimeSpan.FromSeconds(11)); // must not throw out of the timer callback

        Assert.Equal([0], h.Backlight.Writes); // fallback level (2) > 0, so it dims
    }

    [Fact]
    public void Start_writes_nothing_and_the_first_input_restores_the_light_a_previous_instance_left_dark()
    {
        using var h = new Harness(level: 3);

        h.Controller.Start();
        Assert.Empty(h.Backlight.Writes);

        h.Controller.OnInternalInput();
        Assert.Equal([3], h.Backlight.Writes);
    }

    [Fact]
    public void Failed_restore_after_start_is_retried_on_the_next_input()
    {
        using var h = new Harness(level: 3);
        h.Controller.Start();

        h.Backlight.Fail = true;
        h.Controller.OnInternalInput();
        Assert.Empty(h.Backlight.Writes);

        h.Backlight.Fail = false; // hardware recovered
        h.Controller.OnInternalInput();
        Assert.Equal([3], h.Backlight.Writes);
    }

    [Fact]
    public void Start_with_the_lid_closed_writes_nothing_even_on_input_until_the_lid_opens()
    {
        using var h = new Harness(level: 3);
        h.Controller.OnLid(closed: true);
        h.Controller.Start();

        h.Controller.OnInternalInput();
        Assert.Empty(h.Backlight.Writes);

        h.Controller.OnLid(closed: false);
        h.Controller.OnInternalInput();
        Assert.Equal([3], h.Backlight.Writes);
    }

    [Fact]
    public void Dims_normally_after_start_when_nobody_types()
    {
        using var h = new Harness(level: 3, timeoutSeconds: 10);
        h.Controller.Start();

        h.Time.Advance(TimeSpan.FromSeconds(11));

        Assert.Equal([0], h.Backlight.Writes);
        Assert.True(h.Controller.IsDimmed);
    }

    [Fact]
    public void Timeout_change_applies_immediately()
    {
        using var h = new Harness(timeoutSeconds: 60);
        h.Time.Advance(TimeSpan.FromSeconds(5));

        h.Settings = h.Settings with { TimeoutSeconds = 10 };
        h.Controller.OnSettingsChanged();
        h.Time.Advance(TimeSpan.FromSeconds(11));

        Assert.Equal([0], h.Backlight.Writes);
    }

    [Fact]
    public void EnsureLit_restores_but_keeps_the_controller_running_in_case_logoff_is_cancelled()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.True(h.Controller.IsDimmed);

        h.Controller.EnsureLit();
        Assert.Equal([0, 2], h.Backlight.Writes);

        h.Time.Advance(TimeSpan.FromSeconds(11)); // still alive: dims again after the idle period
        Assert.Equal([0, 2, 0], h.Backlight.Writes);
    }

    [Fact]
    public void Shutdown_does_not_write_while_the_lid_is_closed()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        h.Controller.OnLid(closed: true);

        h.Controller.Shutdown();

        Assert.Equal([0], h.Backlight.Writes);
    }

    [Fact]
    public void Lid_opening_after_a_dim_marks_the_state_unknown_so_input_restores()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        h.Controller.OnLid(closed: true);
        h.Controller.OnLid(closed: false);

        h.Controller.OnInternalInput();

        Assert.Equal([0, 2], h.Backlight.Writes);
    }

    [Fact]
    public void A_failed_dim_write_is_retried_after_the_next_period()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Backlight.Fail = true;
        h.Time.Advance(TimeSpan.FromSeconds(11));
        Assert.False(h.Controller.IsDimmed);
        Assert.Empty(h.Backlight.Writes);

        h.Backlight.Fail = false;
        h.Time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal([0], h.Backlight.Writes);
        Assert.True(h.Controller.IsDimmed);
    }

    [Fact]
    public void Session_disconnect_with_the_lid_closed_writes_nothing()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));
        h.Controller.OnLid(closed: true);

        h.Controller.OnSessionDisconnected(true);

        Assert.Equal([0], h.Backlight.Writes);
    }

    [Fact]
    public void Failed_restore_on_session_disconnect_is_retried_on_next_input()
    {
        using var h = new Harness(timeoutSeconds: 10);
        h.Time.Advance(TimeSpan.FromSeconds(11));

        h.Backlight.Fail = true;
        h.Controller.OnSessionDisconnected(true);
        Assert.True(h.Controller.IsDimmed);

        h.Backlight.Fail = false;
        h.Controller.OnInternalInput();
        Assert.False(h.Controller.IsDimmed);
        Assert.Equal([0, 2], h.Backlight.Writes);
    }
}

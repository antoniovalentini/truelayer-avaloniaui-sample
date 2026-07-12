# NavigationPage: outgoing page glitches (loses styling, jumps layout) for the entire duration of the pop animation

## Describe the bug

When popping a page off a `NavigationPage`'s navigation stack (back button or
`PopAsync()`), the outgoing page visibly glitches for the whole duration of
the pop transition, in two independent ways that share the same root cause:

1. **Styling loss.** It loses **all** styling — theme resources resolved via
   `DynamicResource`, and control styles from `Application.Styles` (e.g.
   `FluentTheme`) — rendering with unstyled/default appearance (white
   background, default black text) while sliding off screen.
2. **Layout jump.** If the destination page has a different
   `NavigationPage.HasNavigationBar` value than the outgoing page (e.g.
   popping from a page with a nav bar back to a root page with
   `HasNavigationBar="False"`), the outgoing page's content instantly jumps
   up/down by the nav bar's height the moment the back button is pressed,
   *before* the slide animation even starts.

Both disappear once the transition completes and the page is finally removed.
The incoming (destination) page is unaffected in either case.

This is purely visual/cosmetic but very noticeable with any dark-themed page,
custom brushes, FluentTheme-styled controls, or a navigation stack that mixes
pages with/without a navigation bar.

### Root cause

In `NavigationPage.ExecutePopCore()`, the popped page is removed from
`LogicalChildren` **synchronously, before the transition animation starts**:

```csharp
// Avalonia.Controls.NavigationPage.ExecutePopCore()
Page page = ((_navigationStack.Count > 0) ? _navigationStack.Pop() : null);
...
ILogical logical = page;
if (logical != null)
{
    base.LogicalChildren.Remove(logical); // <-- sets page.Parent = null here
}
...
UpdateActivePage(); // <-- transition/animation is kicked off after this
```

Removing a `StyledElement` from `LogicalChildren` sets its logical `Parent`
to `null` (`StyledElement.ISetLogicalParent.SetParent`), which severs the
chain that `DynamicResource` walks to reach `Application.Resources` /
`Application.Styles`.

`UpdateActivePage()` then keeps that same page instance mounted in
`_pagePresenter` for the entire pop transition — that's how the slide
animation works, the outgoing page stays rendered on top while it animates
off screen:

```csharp
// Avalonia.Controls.NavigationPage.UpdateActivePage()
if (isPop)
{
    pagePresenter.ZIndex = 1;      // outgoing page, still showing `page`
    pageBackPresenter.ZIndex = 0;  // incoming/destination page
}
...
_lastPageTransitionTask = RunPageTransitionAsync(pageTransition, pagePresenter, pageBackPresenter, !isPop, cancellationTokenSource.Token);
```

So for the whole animation, the outgoing page is **visually attached and
animating**, but **logically orphaned** — `DynamicResource` lookups on it
fail to resolve (no path to `Application.Resources`), and styles from
`Application.Styles` stop applying to it too.

The fix should keep the popped page's logical parent intact until the pop
transition has actually finished (i.e. remove it from `LogicalChildren`
after `UpdateActivePage()`/the transition completes, not before).

### Root cause (layout jump)

The same `UpdateActivePage()` call also immediately recomputes nav-bar state
from the **destination** page, before the transition runs:

```csharp
// Avalonia.Controls.NavigationPage.UpdateActivePage()
SetCurrentValue(ContentProperty, result);
SetCurrentValue(Page.CurrentPageProperty, result); // result = new stack top (destination)
...
UpdateIsNavBarEffectivelyVisible();
UpdateBarLayoutBehaviorEffective();
UpdateEffectiveBarHeight();
```

```csharp
// Avalonia.Controls.NavigationPage.UpdateIsNavBarEffectivelyVisible()
bool isNavBarEffectivelyVisible = (base.CurrentPage != null)
    ? GetHasNavigationBar(base.CurrentPage)   // reads the *destination* page's HasNavigationBar
    : _hasHadFirstPage;
IsNavBarEffectivelyVisible = isNavBarEffectivelyVisible;
UpdateNavBarSpacer(); // toggles the `:nav-bar-inset` pseudo-class that reserves space for the bar
```

If the destination page's `NavigationPage.HasNavigationBar` differs from the
outgoing page's, `IsNavBarEffectivelyVisible`/`:nav-bar-inset` flips
instantly — collapsing or expanding the space reserved for the nav bar in the
shared content area. Since the outgoing page is still rendered in the same
content host for the whole transition (see above), its content jumps
immediately, then the page slides off screen on top of the already-shifted
content.

## To Reproduce

1. Create a `NavigationPage` with a root `Page`.
2. `PushAsync` a second `Page` that sets `Background`/`Foreground` etc. via
   `DynamicResource` (e.g. a custom dark theme color defined in
   `Application.Resources`), and/or relies on `Application.Styles` (e.g.
   default `FluentTheme` control templates).
3. Trigger a pop (back button, swipe-back gesture, or `PopAsync()`).
4. Watch the pushed page during the pop animation.

Minimal repro snippet:

```xml
<!-- App.axaml -->
<Application.Resources>
  <SolidColorBrush x:Key="Charcoal" Color="#1f1f25"/>
</Application.Resources>
```

```xml
<!-- SecondPage.axaml -->
<ContentPage Background="{DynamicResource Charcoal}">
  <TextBlock Text="Hello" Foreground="White"/>
</ContentPage>
```

Push `SecondPage`, then pop it — during the pop animation the background
flashes to the default (unstyled) color instead of staying `Charcoal` until
it's off screen.

For the layout jump: give the root page `NavigationPage.HasNavigationBar="False"`
and the pushed page the default (`True`). Push, then pop — the pushed page's
content jumps up the instant you tap back, before it starts sliding away.

## Expected behavior

The popped page should keep its resolved styles/resources and its layout
(nav bar spacing) unchanged, looking visually identical for the entire pop
transition, only being removed from the tree once it is no longer visible
(transition complete) — mirroring how the push transition doesn't cause the
incoming page to render unstyled or jump.

## Avalonia version

12.0.5

## OS

- [x] Linux
- [ ] Windows
- [ ] macOS
- [ ] WebAssembly
- [ ] Android
- [ ] iOS
- [ ] Tizen

## Additional context

- Found while building a mobile-style app (`net10.0`) using
  `NavigationPage`/`TabbedPage`/`Page` with a `NavigationPage`-wrapped tab
  pushing a details page.
- Root cause identified by decompiling `Avalonia.Controls.dll` 12.0.5 with
  `ilspycmd` (`NavigationPage.ExecutePopCore`, `NavigationPage.UpdateActivePage`,
  `NavigationPage.UpdateIsNavBarEffectivelyVisible`), and `Avalonia.Base.dll`
  (`StyledElement.ISetLogicalParent.SetParent`) confirming that removing a
  control from `LogicalChildren` nulls out its logical `Parent`, which is
  what `DynamicResource` resolution and style application walk to reach
  `Application`-level resources/styles.
- Both symptoms share one architectural cause: `NavigationPage` mutates
  state derived from the *destination* page (logical parenting, nav bar
  visibility/height/inset behavior) synchronously when the pop starts,
  instead of deferring it until the pop transition has finished.
- Workaround for the styling loss: use `StaticResource` instead of
  `DynamicResource` for any resource that doesn't need to change at runtime
  on a page that can be popped — `StaticResource` is resolved once at load
  time and isn't affected by the temporary de-parenting.
- No clean workaround found for the layout jump: giving every page in the
  stack the same `HasNavigationBar`/`BarLayoutBehavior` avoids the jump but
  changes the intended UI (e.g. forces a nav bar onto a root page that
  shouldn't have one).

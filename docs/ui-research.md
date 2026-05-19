# UI Research Notes

## Sources Checked

- Microsoft WPF styles and templates documentation: use shared styles and templates to keep visual behavior centralized.
- Microsoft WPF ResourceDictionary documentation: keep reusable resources in dictionaries and merge them from `App.xaml`.
- Microsoft WPF performance guidance: avoid expensive high-frequency visual tree churn, be careful with effects and dynamic resource usage on frequently updated elements.
- Microsoft Segoe Fluent Icons documentation: Windows 11 recommends `Segoe Fluent Icons` as the system symbol icon font, with `Segoe MDL2 Assets` as a fallback.
- Lucide Icons license check: Lucide is ISC licensed and suitable as a lightweight path source if future icons are needed. No new Lucide paths were copied in this pass.
- ui-ux-pro-max design-system query: `desktop utility game controller mapping iOS minimal premium lightweight WPF`.
- ui-ux-pro-max UX query: `desktop utility settings accessibility animation`.
- ui-ux-pro-max style query: `minimal premium desktop utility`.
- ui-ux-pro-max color query: `minimal utility dashboard`.

## Framework Decision

- WPF UI: not introduced. It would add a larger visual framework than this pass needs.
- ReactiveUI: not introduced. Current bindings and commands already cover the UI work; adding a reactive stack would increase migration cost without solving a current blocker.
- Current approach: keep WPF-native `ResourceDictionary`, `Style`, existing `UserControl`, and ViewModel bindings.

## Product Direction

- Visual reference: Apple Settings / Raycast / Linear style.
- Implementation rule: neutral surfaces first, restrained accent only for primary actions and selected state.
- Adopted from ui-ux-pro-max: minimal direct structure, strong whitespace, no emoji icons, stable hover states, 150-300ms micro-interactions.
- Rejected from ui-ux-pro-max for this app: heavy Liquid Glass, morphing effects, dynamic blur, chromatic aberration, long animations. These are too expensive and visually noisy for a high-frequency controller input utility.
- Formal icons: keep vector `Geometry` / `Path`; do not use generated PNG icon sprites.
- Updated formal navigation/action icons from hand-authored XAML paths to Windows system font icons: `Segoe Fluent Icons, Segoe MDL2 Assets`. This removes the rough custom-drawn geometry while keeping the UI lightweight and dependency-free.
- Generated images: reference or brand candidate only after alpha review.

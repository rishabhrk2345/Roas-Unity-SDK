# Basic Integration Sample

`RoasSampleUsage.cs` shows every public call in the SDK: initializing, identifying a user,
tracking a funnel event, verifying a purchase on each platform, and forwarding a deep link.

Import via Package Manager → ROASSensor SDK → Samples → Basic Integration, then attach
`RoasSampleUsage` to a GameObject in your bootstrap scene and set its `publicKey` field (or
delete the `Awake()` call entirely if you've configured `Assets/Resources/RoasSettings.asset`
with `Auto Initialize` on).

#ifndef RoasNative_h
#define RoasNative_h

#ifdef __cplusplus
extern "C" {
#endif

/// Caller owns and must free() the returned string (matches Unity's own
/// convention for marshalled `char*` returns from a native iOS plugin).
const char *_roas_advertisingIdentifier(void);
const char *_roas_identifierForVendor(void);
const char *_roas_appAccountToken(const char *visitorId);

/// 0 = notDetermined, 1 = restricted, 2 = denied, 3 = authorized (ATTrackingManager values).
int _roas_trackingAuthorizationStatus(void);

/// Fires `gameObjectName`'s `onTrackingAuthorizationResult` message once the user has
/// answered (or immediately, if a status already exists) with the resulting status as a
/// string. Fire-and-forget from the C# side — Unity's SendMessage bridge is how the async
/// ATT prompt result gets back onto the main thread's message queue.
void _roas_requestTrackingAuthorization(const char *gameObjectName);

/// Apple Search Ads attribution token (iOS 14.3+, AdServices). NULL if unavailable.
const char *_roas_appleSearchAdsToken(void);

/// SKAdNetwork / AdAttributionKit conversion value update. `coarse` may be NULL.
void _roas_updateConversionValue(int value, const char *coarse, int lockWindow);

#ifdef __cplusplus
}
#endif

#endif

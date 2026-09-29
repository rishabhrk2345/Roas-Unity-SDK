// ROASSensor Unity SDK — iOS native bridge.
//
// Kept deliberately thin: every rule (raw signal names not verdicts, IDFA respects ATT,
// appAccountToken must be a real UUID not the "rs..." vid string) mirrors sdk-ios's
// Roas.swift / DeviceContext.swift in the main roas-sensor-service repo — read those before
// changing behaviour here, so the two never drift for a customer comparing native iOS and
// Unity-iOS builds in one dashboard.
//
// Frameworks this file needs linked into the Xcode project (added automatically by
// Editor/RoasIOSPostProcessBuild.cs at build time — do not add manually):
//   AdSupport.framework, AppTrackingTransparency.framework, AdServices.framework,
//   StoreKit.framework
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<AdSupport/AdSupport.h>)
#import <AdSupport/AdSupport.h>
#endif
#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#endif
#if __has_include(<AdServices/AdServices.h>)
#import <AdServices/AdServices.h>
#endif
#if __has_include(<StoreKit/StoreKit.h>)
#import <StoreKit/StoreKit.h>
#endif

extern "C" {
    extern void UnitySendMessage(const char *obj, const char *method, const char *msg);
}

static const char *roas_strdup_or_null(NSString *value) {
    if (value == nil) return NULL;
    const char *utf8 = [value UTF8String];
    if (utf8 == NULL) return NULL;
    return strdup(utf8);
}

extern "C" const char *_roas_advertisingIdentifier(void) {
#if __has_include(<AdSupport/AdSupport.h>)
    // Respect the user's choice exactly like the native SDK does: a denied/undetermined
    // ATT status means "no signal", never a placeholder or all-zero UUID passed off as one.
    if (@available(iOS 14, *)) {
        if ([ATTrackingManager trackingAuthorizationStatus] != ATTrackingManagerAuthorizationStatusAuthorized) {
            return NULL;
        }
    }
    ASIdentifierManager *manager = [ASIdentifierManager sharedManager];
    if (!manager.isAdvertisingTrackingEnabled && ![ATTrackingManager class]) return NULL;
    NSUUID *idfa = manager.advertisingIdentifier;
    if (idfa == nil) return NULL;
    return roas_strdup_or_null([idfa UUIDString]);
#else
    return NULL;
#endif
}

extern "C" const char *_roas_identifierForVendor(void) {
    NSUUID *idfv = [[UIDevice currentDevice] identifierForVendor];
    if (idfv == nil) return NULL;
    return roas_strdup_or_null([idfv UUIDString]);
}

extern "C" const char *_roas_appAccountToken(const char *visitorId) {
    // Mirrors Roas.swift's appAccountToken(): the vid is "rs" + 32 hex chars, folded
    // deterministically into a UUID so StoreKit's appAccountToken (which MUST be a real
    // UUID, unlike Android's Play Billing obfuscatedAccountId) still round-trips to the
    // same install. Returns NULL for anything that doesn't fit that shape.
    if (visitorId == NULL) return NULL;
    NSString *vid = [NSString stringWithUTF8String:visitorId];
    if (![vid hasPrefix:@"rs"] || vid.length != 34) return NULL;
    NSString *hex = [[vid substringFromIndex:2] uppercaseString];
    NSString *uuidString = [NSString stringWithFormat:@"%@-%@-%@-%@-%@",
        [hex substringWithRange:NSMakeRange(0, 8)],
        [hex substringWithRange:NSMakeRange(8, 4)],
        [hex substringWithRange:NSMakeRange(12, 4)],
        [hex substringWithRange:NSMakeRange(16, 4)],
        [hex substringFromIndex:20]];
    NSUUID *uuid = [[NSUUID alloc] initWithUUIDString:uuidString];
    if (uuid == nil) return NULL;
    return roas_strdup_or_null(uuidString);
}

extern "C" int _roas_trackingAuthorizationStatus(void) {
#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
    if (@available(iOS 14, *)) {
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    }
#endif
    return 3; // pre-iOS 14: no ATT gate, treat as authorized (matches IDFA always being readable)
}

extern "C" void _roas_requestTrackingAuthorization(const char *gameObjectName) {
    NSString *objectName = [NSString stringWithUTF8String:gameObjectName];
#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
    if (@available(iOS 14, *)) {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            dispatch_async(dispatch_get_main_queue(), ^{
                NSString *statusString = [NSString stringWithFormat:@"%ld", (long)status];
                UnitySendMessage([objectName UTF8String], "OnTrackingAuthorizationResult", [statusString UTF8String]);
            });
        }];
        return;
    }
#endif
    UnitySendMessage([objectName UTF8String], "OnTrackingAuthorizationResult", "3");
}

extern "C" const char *_roas_appleSearchAdsToken(void) {
#if __has_include(<AdServices/AdServices.h>)
    if (@available(iOS 14.3, *)) {
        NSError *error = nil;
        NSString *token = [AAAttribution attributionTokenWithError:&error];
        if (error != nil || token == nil) return NULL;
        return roas_strdup_or_null(token);
    }
#endif
    return NULL;
}

extern "C" void _roas_updateConversionValue(int value, const char *coarse, int lockWindow) {
#if __has_include(<StoreKit/StoreKit.h>)
    NSString *coarseString = (coarse != NULL) ? [NSString stringWithUTF8String:coarse] : nil;
    if (@available(iOS 16.1, *)) {
        if (coarseString != nil) {
            SKAdNetworkCoarseConversionValue coarseValue = SKAdNetworkCoarseConversionValueLow;
            if ([coarseString isEqualToString:@"medium"]) coarseValue = SKAdNetworkCoarseConversionValueMedium;
            else if ([coarseString isEqualToString:@"high"]) coarseValue = SKAdNetworkCoarseConversionValueHigh;
            if (lockWindow) {
                [SKAdNetwork updatePostbackConversionValue:value coarseValue:coarseValue lockWindow:YES completionHandler:nil];
            } else {
                [SKAdNetwork updatePostbackConversionValue:value coarseValue:coarseValue lockWindow:NO completionHandler:nil];
            }
        } else if (lockWindow) {
            [SKAdNetwork updatePostbackConversionValue:value completionHandler:nil];
        } else {
            [SKAdNetwork updatePostbackConversionValue:value completionHandler:nil];
        }
        return;
    }
    if (@available(iOS 15.4, *)) {
        [SKAdNetwork updatePostbackConversionValue:value completionHandler:nil];
        return;
    }
    if (@available(iOS 11.3, *)) {
        [SKAdNetwork updateConversionValue:value];
    }
#endif
}

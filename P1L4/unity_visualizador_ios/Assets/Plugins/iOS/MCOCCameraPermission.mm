#import <AVFoundation/AVFoundation.h>
#import <Foundation/Foundation.h>
#import <dispatch/dispatch.h>
#include <atomic>

static std::atomic<bool> sCameraRequestPending(false);

extern "C" int MCOC_CameraAuthorizationStatus()
{
    NSString *description = [[NSBundle mainBundle] objectForInfoDictionaryKey:@"NSCameraUsageDescription"];
    if (![description isKindOfClass:[NSString class]] || description.length == 0)
        return -1;
    return (int)[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
}

extern "C" int MCOC_CameraAuthorizationPending()
{
    return sCameraRequestPending.load() ? 1 : 0;
}

extern "C" void MCOC_RequestCameraAuthorization()
{
    if (MCOC_CameraAuthorizationStatus() != (int)AVAuthorizationStatusNotDetermined)
        return;
    bool expected = false;
    if (!sCameraRequestPending.compare_exchange_strong(expected, true))
        return;
    dispatch_async(dispatch_get_main_queue(), ^{
        NSLog(@"[MCOC Camera] Requesting video permission. bundle=%@ state=%d",
              [NSBundle mainBundle].bundleIdentifier, MCOC_CameraAuthorizationStatus());
        [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted) {
            NSLog(@"[MCOC Camera] Permission completed. granted=%d state=%d",
                  granted, MCOC_CameraAuthorizationStatus());
            sCameraRequestPending.store(false);
        }];
    });
}

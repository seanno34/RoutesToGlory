#import <AVFoundation/AVFoundation.h>

extern "C" void RTG_EnablePlaybackAudioSession()
{
    AVAudioSession *session = [AVAudioSession sharedInstance];
    NSError *error = nil;
    [session setCategory:AVAudioSessionCategoryPlayback
             withOptions:AVAudioSessionCategoryOptionMixWithOthers
                   error:&error];
    if (error == nil)
    {
        [session setActive:YES error:&error];
    }
}

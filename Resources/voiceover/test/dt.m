// Dry test of the speech bridge: prints which voice/rate/volume each message would use.
#import <Cocoa/Cocoa.h>
void hsa_vo_init(void); int hsa_vo_output(const char*, int);
int main(){ @autoreleasepool { [NSApplication sharedApplication]; hsa_vo_init();
  dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 2*NSEC_PER_SEC), dispatch_get_main_queue(), ^{
    hsa_vo_output("Okrzyk bojowy: Efekt wywoływany po zagraniu karty.", 0);
    hsa_vo_output("Battlecry: Deal 2 damage to an enemy minion.", 0);
    hsa_vo_output("Ręka", 1); });
  dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 5*NSEC_PER_SEC), dispatch_get_main_queue(), ^{ exit(0); });
  [[NSRunLoop mainRunLoop] run]; } }

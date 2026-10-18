// Tells the Unity build whether the page is being viewed on a touch device (phone / tablet),
// so TouchControls only draws the on-screen pad there and never on a desktop browser.
// A phone or tablet user agent counts, and so does a device that reports several touch points
// with a coarse pointer and no fine pointer (iPadOS Safari claims to be a Mac, and a Windows
// laptop with a touchscreen also reports a fine pointer, so it is treated as a desktop).
var CrucibleDevicePlugin = {
  CrucibleIsTouchDevice: function () {
    try {
      var ua = navigator.userAgent || '';
      var mobileUA = /Android|iPhone|iPad|iPod|IEMobile|Opera Mini|Mobile|Tablet|Silk|Kindle|PlayBook|BB10/i.test(ua);
      var points = (navigator.maxTouchPoints || 0) > 1;
      var coarse = !!(window.matchMedia && window.matchMedia('(pointer: coarse)').matches);
      var fine = !!(window.matchMedia && window.matchMedia('(any-pointer: fine)').matches);
      return (mobileUA || (points && coarse && !fine)) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  }
};
mergeInto(LibraryManager.library, CrucibleDevicePlugin);

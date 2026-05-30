# Misfits Change - wasteland theme: shuttle → train
# Commands
## Delay shuttle round end
#emergency-shuttle-command-round-desc = Stops the timer that ends the round when the emergency shuttle exits hyperspace.
emergency-shuttle-command-round-desc = Stops the timer that ends the round when the train departs the wasteland.
emergency-shuttle-command-round-yes = Round delayed.
emergency-shuttle-command-round-no = Unable to delay round end.

## Dock emergency shuttle
#emergency-shuttle-command-dock-desc = Calls the emergency shuttle and docks it to the station... if it can.
emergency-shuttle-command-dock-desc = Calls the train and docks it to the station... if it can.

## Launch emergency shuttle
#emergency-shuttle-command-launch-desc = Early launches the emergency shuttle if possible.
emergency-shuttle-command-launch-desc = Early launches the train if possible.

# Emergency shuttle
#emergency-shuttle-left = The Emergency Shuttle has left the station. Estimate {$transitTime} seconds until the shuttle arrives at CentCom.
emergency-shuttle-left = The train has left the station. Estimate {$transitTime} seconds until it clears the region.
#emergency-shuttle-launch-time = The emergency shuttle will launch in {$consoleAccumulator} seconds.
emergency-shuttle-launch-time = The train will depart in {$consoleAccumulator} seconds.
#emergency-shuttle-docked = The Emergency Shuttle has docked {$direction} of the station, {$location}. It will leave in {$time} seconds.
emergency-shuttle-docked = The train has arrived {$direction} of the station, {$location}. It will depart in {$time} seconds.
#emergency-shuttle-good-luck = The Emergency Shuttle is unable to find a station. Good luck.
emergency-shuttle-good-luck = The train is unable to find the station. Good luck out there.
#emergency-shuttle-nearby = The Emergency Shuttle is unable to find a valid docking port. It has warped {$direction}.
emergency-shuttle-nearby = The train is unable to find a valid docking port. It has rerouted {$direction}.

# Emergency shuttle console popup / announcement
emergency-shuttle-console-no-early-launches = Early departure is disabled
#emergency-shuttle-console-auth-left = {$remaining} authorizations needed until shuttle is launched early.
emergency-shuttle-console-auth-left = {$remaining} authorizations needed until train departs early.
#emergency-shuttle-console-auth-revoked = Early launch authorization revoked, {$remaining} authorizations needed.
emergency-shuttle-console-auth-revoked = Early departure authorization revoked, {$remaining} authorizations needed.
emergency-shuttle-console-denied = Access denied

# UI
#emergency-shuttle-console-window-title = Emergency Shuttle Console
emergency-shuttle-console-window-title = Train Console
emergency-shuttle-ui-engines = ENGINES:
emergency-shuttle-ui-idle = Idle
emergency-shuttle-ui-repeal-all = Repeal All
#emergency-shuttle-ui-early-authorize = Early Launch Authorization
emergency-shuttle-ui-early-authorize = Early Departure Authorization
emergency-shuttle-ui-authorize = AUTHORIZE
emergency-shuttle-ui-repeal = REPEAL
emergency-shuttle-ui-authorizations = Authorizations
emergency-shuttle-ui-remaining = Remaining: {$remaining}

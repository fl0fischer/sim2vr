# SIM2VR: Towards Automated Biomechanical Testing in VR

SIM2VR is a novel system for **integrating biomechanical simulations directly into Unity applications**.

By ensuring that real and simulated users see and control exactly the same virtual environment, it increases the ecological validity of simulation-based predictions.  
SIM2VR can be used to predict and analyse differences in performance and ergonomics between specific design choices, and to anticipate potential user strategies for a given VR application.

## Requirements and Scope
SIM2VR requires that the Unity application has an OpenXR plugin (min. version 1.5.3) to handle the VR device interaction.  
For the user simulation, any simulated user instance created within the [UitB framework](https://github.com/aikkala/user-in-the-box) can be used. Note that this requires the biomechanical model to be implemented in the [MuJoCo physics engine](https://mujoco.org/).

The current focus of SIM2VR is on movement-based VR interaction using a VR controller and an HMD. Since the UitB framework only includes visual and proprioceptive sensor modalities, SIM2VR is currently limited to the transmission of visual output signals from the VR application to the simulated user. However, we plan to support other feedback modalities such as auditory and haptic output in the future.

## Step-by-Step Guide
In the following, we demonstrate how SIM2VR can be used to generate user simulations for a given Unity application.
As an example, we consider the [Beat Saber](https://beatsaber.com/)-style game implemented in the [VR Beats Kit](https://assetstore.unity.com/packages/templates/systems/vr-beats-kit-168243), which is freely available on the Unity Asset Store (from here on referred to as _Saber Game_).

### Initial Steps
First, the SIM2VR Asset must be imported into the desired Unity project.
After adding the _sim2vr_ prefab as a game object to the desired scene, the _SimulatedUser_ game object needs to be connected to the VR Controllers and Main Camera provided by OpenXR.
The _sim2vr_ prefab comprises three distinct game objects: _SimulatedUser_, _RLEnv_ and _Logger_.  

The _SimulatedUser_ must be given access to the Transforms of the _LeftHandController_/_RightHandController_ and the _MainCamera_ of OpenXR by connecting them to the corresponding fields.

### Defining the Game Reward and Reset
To run user simulations with the Unity application, appropriate reward and reset methods need to be defined. 
For this purpose, an application-specific class must be inherited from the _RLEnv_ class provided by the SIM2VR asset.  
Note that all game objects and variables relevant for the reward calculation must be accessible from this class.
For example, if the distance of a game object to the controller is used as a reward, the game object's and controller's positions should be fields of this class.

The task-specific reward needs to be computed by the method _CalculateReward_ and stored in the variable _\_reward_. If a game score is provided by the VR application, this score can be directly used as reward (note that game scores typically accumulate points throughout the round, so the reward signal should be set to the increase in that score since the last frame). If necessary, the typically sparse game reward can be augmented by additional, more sophisticated terms, as described in the accompanying paper.  
In the Saber Game, we set the reward to the increase in the game score since the last frame.

The method _Reset_ needs to ensure that the entire scene is reset to a (reproducible) initial state at the end of each round. This usually includes the destruction of game objects created during runtime and resetting private variables required to compute the game reward. All code related to resetting the game reward can be summarised in the method _InitialiseReward_.  
Preparations for the next round, such as choosing a game level or defining variables required for the reward calculations, can also be defined here.
Actions and settings that should be taken only once when starting the game can be defined in the method _InitialiseApplication_.  
For the Saber Game, it is sufficient to simply invoke the existing _onRestart_ game event and set the current game score for reward calculation to 0 in the method _Reset_, which triggers the restart of the level in the _VR\_BeatManager_ and the _ScoreManager_.

Finally, the Simulated User needs to be informed about whether the current round has ended, i.e., the variable _\_isFinished_ needs to be updated accordingly within the method _UpdateIsFinished_.  
For the Saber Game, we can make use of the method _getIsGameRunning_ of the _VR\_BeatManager_ instance.

### Further Adjustments
Since including an application- and task-dependent time feature as "stateful" information in the observation may help for training the RL agent, the RLEnv class provides a method _GetTimeFeature_ to define this time feature.  
For the Saber Game, we set this to the relative in-game time normalized to values between -1 and 1, as this might help the RL Agent anticipating the deterministic target sequence for a given song. Note that this requires access to the maximum duration of the round.

Often, a Unity application commences with an initial scene, such as a menu, rather than directly starting the game.
As SIM2VR does not provide a mechanism to switch scenes, this needs to be manually implemented.  
In our example, we simply added a game object containing a script that selects the appropriate level and transitions to the _SaberStyle_ scene at the startup of the application.

Since the biomechanical models currently provided by UitB are limited to movements of the right arm, we modify the game to spawn targets for the right saber only. Also, since the Simulated User is unable to duck, we removed the walls that moved towards the player, as these would have ended the game immediately if they were touched by the HMD.

The _Logger_ is optional and can be used to log individual trials, for example, when collecting data from a user study. 
If this is not intended, it should be disabled.

### Building the Unity Application
From the resulting Unity project augmented by the SIM2VR scripts and game objects, a standalone Unity Application can be built. This application is then used as interaction environment for the simulated user during training.

### Defining the Simulated User in UitB
After preparing the VR Interaction environment for running user simulations, a simulated user instance needs to be created in UitB. 

All relevant information can be defined in the YAML config file (see [here](https://github.com/aikkala/user-in-the-box/tree/main?tab=readme-ov-file#building-a-simulator)).
This mainly involves:
- selecting a biomechanical user model (_bm\_model_), including the effort model (_effort\_model_) and effort cost weight (_weight_)
- selecting perception modules, including the  _vision.UnityHeadset_  module provided by SIM2VR (_perception\_modules_)
- providing the path of the standalone Unity Application to interact with (_unity\_executable_)

Other optional parameters include:
- optional arguments to be passed to the VR application (e.g., to set a specific game level or difficulty) (_app\_args_)
- the VR hardware setup (_gear_)
- the position and orientation of the VR controllers (_left\_controller\_relpose_, _right\_controller\_relpose_) relative to a body part included in the biomechanical user model (_left\_controller\_body_, _right\_controller\_body_)
- the position and orientation of the HMD (_headset\_relpose_) relative to a body part included in the biomechanical user model (_headset\_body_)
- RL hyperparameters (e.g., network size, time steps, batch size, etc.)

For the Saber Game, we use the muscle-actuated right arm and upper body model _MoblArmsWrist_ with neural effort costs, which penalize the sum of the squared muscle control signals at each time step. As perception modules, we use the default proprioception module and the _UnityHeadset_ vision module provided by SIM2VR. The former allows the simulated user to infer joint angles, velocities and accelerations, as well as muscle activations and index finger position. The latter is configured to include red and blue color channels of the RGB-D image, and stacked with a delayed (0.2 seconds prior) visual observation to allow the control policy to infer object movement.

### Training and Evaluation
The training can then be started by running the UitB Python script [_uitb/train/trainer.py_](https://github.com/aikkala/user-in-the-box/blob/main/uitb/train/trainer.py) and passing the configuration file as an argument.

Similarly, a trained simulator can be evaluated using the standard UitB script [_uitb/test/evaluator.py_](https://github.com/aikkala/user-in-the-box/blob/main/uitb/test/evaluator.py). The script runs the simulator/policy and optionally saves log files and videos of the evaluated episodes.

For better logging and evaluation, we recommend to connect a [Weights and Biases](https://wandb.ai/) account to the trainer.

## Contributors
Florian Fischer*  
Aleksi Ikkala*  
Markus Klar  
Arthur Fleig  
Miroslav Bachinski  
Roderick Murray-Smith  
Perttu Hämäläinen  
Antti Oulasvirta  
Jörg Müller  

_(*equal contribution)_

## Citation
Please cite the following paper when using SIM2VR:
**TODO** add paper link

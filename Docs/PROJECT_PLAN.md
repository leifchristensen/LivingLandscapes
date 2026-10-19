# PROJECT PLAN: LivingLandscapes Mod - Gameplay & Technical Implementation
**Author:** Leif Christensen (with Claude)  
**Date:** 2026-10-19  
**Status:** Active

## 1. Goals & Objectives
- Improve map generation to be more gameplay-friendly while maintaining geological realism.
- Introduce data-driven configuration (XML) to tune parameters without code changes.
- Ensure maps have natural resource clustering, accessibility, and defensive terrain features.

## 2. Current Status
- **Issue #8**: Configurable Gameplay Bias XML Structure created.
- **Issue #9**: Biome-Specific Resource Density Matrices planned.
- **Current Focus**: Developing the XML structure and parser for gameplay bias configuration.

## 3. Key Decisions & Constraints
- **Approach**: Data-driven, algorithmic, with natural randomness (no hard-coded map layouts).
- **Workflow**: Frequent commits, descriptive messages, and using `PROJECT_PLAN.md` as a checkpoint.
- **Collaboration**: Use Git history and plan file to track progress across context resets.

## 4. Technical Implementation Steps
1. **XML Structure (`GameplayBias.xml`)**: Define weights for resource density, erosion strength, and landmark clustering.
2. **Parser Integration**: Update the main mod class to load these values at runtime.
3. **Pipeline Updates**: Implement a pass in `LandscapeGenerator` that applies these biases (e.g., smoothing valleys along water flow, boosting resource density based on biome).
4. **Testing**: Verify that maps look natural and are playable (accessible resources, good defensive positions).

## 5. Workflow Instructions for Claude Code
- Commit frequently with messages like `WIP: <task>` or `Fix: <bug>`.
- Check `PROJECT_PLAN.md` before starting new sessions to ensure alignment.
- If context limits hit, pause and summarize progress in the plan file before resuming.

## 6. Next Steps
- Finalize XML structure definition.
- Implement parser for `GameplayBias.xml`.
- Integrate bias logic into `LandscapeGenerator` pipeline.
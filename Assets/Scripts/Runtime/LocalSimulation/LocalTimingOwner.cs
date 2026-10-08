using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using OverTheHorizon.Timing;
using OverTheHorizon.Player.Input;

namespace OverTheHorizon.LocalSimulation
{
    public sealed class LocalTimingOwner : IDisposable
    {
        private static LocalTimingOwner active;
        private readonly float originalFixed, originalMaximum, originalScale;
        private readonly InputSettings.UpdateMode originalInputMode;
        private float appliedFixed, appliedMaximum, appliedScale;
        private bool fixedApplied, maximumApplied, scaleApplied, inputApplied;
        private bool disposed;
        private bool failedAcquisition;
        internal static bool HasFailedAcquisition => active != null && active.failedAcquisition;
        public bool IsActive => !disposed && active == this;
        public LocalTimingOwner(SimulationTimingConfig config, float maximumDeltaTime)
        {
            if (config.Version < 1 || config.FixedDeltaTime <= 0) throw new ArgumentException("Constructed timing configuration required.");
            if (active != null) throw new InvalidOperationException("Local timing already has an owner.");
            if (Physics.simulationMode != SimulationMode.FixedUpdate)
                throw new InvalidOperationException("This adapter requires automatic 3D FixedUpdate physics.");
            if (maximumDeltaTime < config.FixedDeltaTime || !PlayerValueValidation.Finite(maximumDeltaTime))
                throw new InvalidOperationException("Invalid maximumDeltaTime.");
            originalFixed = Time.fixedDeltaTime; originalMaximum = Time.maximumDeltaTime; originalScale = Time.timeScale;
            originalInputMode = InputSystem.settings.updateMode;
            active = this;
            try
            {
                Time.fixedDeltaTime = config.FixedDeltaTime;
                appliedFixed = Time.fixedDeltaTime; fixedApplied = true;
                if (!PlayerValueValidation.Finite(appliedFixed) || appliedFixed <= 0
                    || Math.Abs((double)appliedFixed - config.FixedDeltaTime) > config.FixedDeltaTime * 1e-6)
                    throw new InvalidOperationException("Unity could not apply the requested fixed timestep.");
                Time.maximumDeltaTime = maximumDeltaTime;
                appliedMaximum = Time.maximumDeltaTime; maximumApplied = true;
                if (!PlayerValueValidation.Finite(appliedMaximum) || appliedMaximum < appliedFixed)
                    throw new InvalidOperationException("Unity could not apply maximumDeltaTime.");
                Time.timeScale = 0; appliedScale = Time.timeScale; scaleApplied = true;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                inputApplied = true;
                Validate();
            }
            catch (Exception error)
            {
                failedAcquisition = true;
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }
        public void SetPaused(bool paused)
        {
            Validate();
            Time.timeScale = paused ? 0f : 1f; appliedScale = Time.timeScale;
        }
        // Fault handling must not overwrite a scale changed by another owner.
        public void PauseIfOwned()
        {
            if (!IsActive || !scaleApplied || Time.timeScale != appliedScale) return;
            Time.timeScale = 0; appliedScale = Time.timeScale;
        }
        public void Validate()
        {
            if (disposed || active != this)
                throw new InvalidOperationException("Timing owner is no longer the active owner.");
            if (Time.fixedDeltaTime != appliedFixed)
                throw new InvalidOperationException("Time.fixedDeltaTime expected " + F(appliedFixed) + ", actual " + F(Time.fixedDeltaTime) + ".");
            if (Time.maximumDeltaTime != appliedMaximum)
                throw new InvalidOperationException("Time.maximumDeltaTime expected " + F(appliedMaximum) + ", actual " + F(Time.maximumDeltaTime) + ".");
            if (Time.timeScale != appliedScale)
                throw new InvalidOperationException("Time.timeScale expected " + F(appliedScale) + ", actual " + F(Time.timeScale) + ". Another system may be changing global pause/time scale.");
            if (Physics.simulationMode != SimulationMode.FixedUpdate)
                throw new InvalidOperationException("Physics.simulationMode expected FixedUpdate, actual " + Physics.simulationMode + ".");
            if (InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                throw new InvalidOperationException("InputSystem updateMode expected ProcessEventsInDynamicUpdate, actual " + InputSystem.settings.updateMode + ".");
        }
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        public void Dispose()
        {
            if (disposed) return;
            var errors = new List<Exception>();
                if (active == this)
                {
                    Restore(() =>
                    {
                        if (!fixedApplied) return;
                        if (Time.fixedDeltaTime != appliedFixed || originalFixed == appliedFixed) { fixedApplied = false; return; }
                        Time.fixedDeltaTime = originalFixed;
                        appliedFixed = Time.fixedDeltaTime;
                        // Setter/getter round trips may quantize again; restoration has the same bounded error contract.
                        if (Math.Abs((double)appliedFixed - originalFixed) > originalFixed * 1e-6)
                            throw new InvalidOperationException("Unity could not restore the original fixed timestep.");
                        fixedApplied = false;
                    }, errors);
                    Restore(() => { if (maximumApplied && Time.maximumDeltaTime == appliedMaximum) Time.maximumDeltaTime = originalMaximum; maximumApplied = false; }, errors);
                    Restore(() => { if (scaleApplied && Time.timeScale == appliedScale && originalScale != appliedScale) Time.timeScale = originalScale; scaleApplied = false; }, errors);
                    Restore(() => { if (inputApplied && InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                        InputSystem.settings.updateMode = originalInputMode; inputApplied = false; }, errors);
                }
            if (errors.Count != 0) throw new AggregateException("Timing restoration failed.", errors);
            if (active == this) active = null;
            disposed = true;
        }
        private static void Restore(Action action, List<Exception> errors)
        { try { action(); } catch (Exception error) { errors.Add(error); } }
        internal static void ReleaseActive() { active?.Dispose(); }
        internal static void RetryFailedAcquisition() { if (HasFailedAcquisition) active.Dispose(); }
    }
}

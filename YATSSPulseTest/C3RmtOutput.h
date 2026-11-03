#pragma once

#include <driver/gpio.h>
#include <driver/rmt_tx.h>
#include "RmtPulseTrain.h"
#include "ManualLevel.h"

// One whole pulse fits in the first half of a C3 RMT memory block, including
// the maximum one-second width. No streaming refill is needed during a pulse.
template <class Output>
class C3RmtOutput {
 public:
  C3RmtOutput(Output& output, const uint8_t* pins) : output_(output), pins_(pins) {}
  uint32_t now() { return output_.now(); }
  void set(uint8_t channel, bool blocked) { output_.set(channel, blocked); }

  bool prepare(const DragPulse::PulsePlan& plan, uint8_t channel) {
    if (!manualLevel_.healthy()) return false;
    if (prepared_ && (pins_[channel] != pin_ || plan.idleHigh != idleHigh_)) return false;
    pin_ = pins_[channel];
    idleHigh_ = plan.idleHigh;
    symbolCount_ = plan.length / 2;
    for (uint8_t i = 0; i < symbolCount_; ++i) {
      symbols_[i].val = 0;
      symbols_[i].duration0 = plan.halves[2 * i].ticks;
      symbols_[i].level0 = plan.halves[2 * i].high;
      symbols_[i].duration1 = plan.halves[2 * i + 1].ticks;
      symbols_[i].level1 = plan.halves[2 * i + 1].high;
    }
    if (prepared_) return enabled_;
    prepared_ = true;
    // Initial peripheral handoff must be performed with the receiver disconnected.
    if (gpio_hold_en(static_cast<gpio_num_t>(pin_)) != ESP_OK) return false;
    rmt_tx_channel_config_t cfg = {};
    cfg.gpio_num = static_cast<gpio_num_t>(pin_);
    cfg.clk_src = RMT_CLK_SRC_DEFAULT;
    cfg.resolution_hz = 1000000;
    cfg.mem_block_symbols = 48;
    cfg.trans_queue_depth = 1;
    cfg.flags.init_level = idleHigh_;
    if (rmt_new_tx_channel(&cfg, &channel_) != ESP_OK) return false;
    rmt_copy_encoder_config_t encoderCfg = {};
    if (rmt_new_copy_encoder(&encoderCfg, &encoder_) != ESP_OK) return false;
    rmt_tx_event_callbacks_t callbacks = {};
    callbacks.on_trans_done = onDone;
    if (rmt_tx_register_event_callbacks(channel_, &callbacks, this) != ESP_OK) return false;
    if (rmt_enable(channel_) != ESP_OK) return false;
    enabled_ = true;
    gpio_hold_dis(static_cast<gpio_num_t>(pin_));
    return true;
  }

  bool fire() {
    if (!manualLevel_.healthy()) return false;
    // Clear before submitting: a short transmission can complete before
    // rmt_transmit returns. The ISR only sets this flag and never logs.
    portENTER_CRITICAL(&completionMux_);
    completed_ = false;
    portEXIT_CRITICAL(&completionMux_);
    rmt_transmit_config_t cfg = {};
    cfg.flags.eot_level = idleHigh_;
    cfg.flags.queue_nonblocking = true;
    return rmt_transmit(channel_, encoder_, symbols_, symbolCount_ * sizeof(symbols_[0]), &cfg) == ESP_OK;
  }
  bool done() {
    portENTER_CRITICAL(&completionMux_);
    const bool complete = completed_;
    portEXIT_CRITICAL(&completionMux_);
    return complete;
  }

  bool setLevel(bool high) {
    if (!prepared_ || !enabled_) return false;
    return manualLevel_.set(high,
      [&](rmt_symbol_word_t* symbol) {
        rmt_transmit_config_t cfg = {};
        cfg.flags.eot_level = high;
        return rmt_transmit(channel_, encoder_, symbol, sizeof(*symbol), &cfg) == ESP_OK;
      },
      [&] { return rmt_tx_wait_all_done(channel_, 1000) == ESP_OK; },
      [&] { if (rmt_disable(channel_) == ESP_OK) enabled_ = false; });
  }

  void release() {
    if (!prepared_ || !manualLevel_.healthy()) return;
    // Retain the RMT pin routing between runs. Only an interrupted transaction
    // needs cancellation; its partial pulse cannot be counted reliably.
    if (enabled_ && !done()) {
      rmt_disable(channel_);
      enabled_ = rmt_enable(channel_) == ESP_OK;
      if (enabled_) setLevel(idleHigh_);
    }
  }

  bool shutdown() {
    if (!manualLevel_.healthy()) return false;
    if (!prepared_) return true;
    gpio_hold_en(static_cast<gpio_num_t>(pin_));
    if (enabled_) rmt_disable(channel_);
    if (channel_) rmt_del_channel(channel_);
    if (encoder_) rmt_del_encoder(encoder_);
    enabled_ = prepared_ = false;
    channel_ = nullptr;
    encoder_ = nullptr;
    pinMode(pin_, OUTPUT);
    digitalWrite(pin_, idleHigh_ ? HIGH : LOW);
    gpio_hold_dis(static_cast<gpio_num_t>(pin_));
    return true;
  }

 private:
  static bool IRAM_ATTR onDone(rmt_channel_handle_t, const rmt_tx_done_event_data_t*, void* context) {
    auto* self = static_cast<C3RmtOutput*>(context);
    portENTER_CRITICAL_ISR(&self->completionMux_);
    self->completed_ = true;
    portEXIT_CRITICAL_ISR(&self->completionMux_);
    return false;
  }

  Output& output_;
  const uint8_t* pins_;
  uint8_t pin_ = 0, symbolCount_ = 0;
  bool prepared_ = false, enabled_ = false, idleHigh_ = false;
  rmt_channel_handle_t channel_ = nullptr;
  rmt_encoder_handle_t encoder_ = nullptr;
  portMUX_TYPE completionMux_ = portMUX_INITIALIZER_UNLOCKED;
  bool completed_ = true;
  rmt_symbol_word_t symbols_[17] = {};
  ManualLevel<rmt_symbol_word_t> manualLevel_;
};

# Eco Diet Optimizer

Eco Diet Optimizer is a server-side mod for Eco that helps players maximize skill gain by suggesting the best balanced diet based on their stomach size and known food preferences. It prioritizes balanced nutrition (25% each of Carbs, Fat, Protein, Vitamins) while also considering food tier and taste.

## Features

*   **Optimal Diet Calculation**: Suggests a diet that maximizes skill points by balancing nutrients and prioritizing high-tier, tasty foods.
*   **Taste Tracking**: Learns player preferences (Favorite, Delicious, Good, Ok, Bad, Horrible) and avoids disliked foods.
*   **Shopping Lists**: Generates shopping lists for multiple meals based on the current suggestion.
*   **Strict Mode**: Can limit suggestions to only foods the player has already discovered/tasted (default) or include all foods.
*   **Admin Tools**: Configurable cooldowns and debug logging.

## Installation

1.  Navigate to your Eco server directory.
2.  Go to `Mods/UserCode`.
3.  Create a new folder named `EcoDietOptimizer`.
4.  Copy the `EcoDietOptimizer.cs` file into this folder.
5.  Restart your server.

## Usage

Players can use the following commands in the chat:

*   `/diet`: Suggests the best balanced diet for 1 meal (uses only tasted foods by default).
*   `/diet [N]`: Generates a shopping list for N meals based on the current suggestion.
    *   Example: `/diet 5` for a 5-meal shopping list.
*   `/diet taste`: Lists your discovered foods grouped by taste preference.
*   `/diet clear`: Clears the currently cached diet suggestion, forcing a recalculation.
*   `/diet strict`: Toggles strict discovery mode.
    *   **On** (Default): Suggests only known/tasted foods.
    *   **Off**: Suggests from all available foods in the game (excluding creative/admin items).
*   `/diet help`: Displays the in-game help message.

## Admin Commands

Server administrators have access to additional configuration commands:

*   `/diet config [minutes]`: Sets the global cooldown period for diet recalculation to the specified number of minutes.
    *   Example: `/diet config 60` sets a 1-hour cooldown.
*   `/diet debug`: Toggles verbose logging to `EcoDietOptimizer_Log.txt`.

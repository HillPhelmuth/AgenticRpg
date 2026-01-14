---
applyTo: 'AIAgenticRpg/**, AIAgenticRpg.Components/**'
---
# UI Overview - AI RPG Agents

## Introduction
This document outlines the UI requirements for an AI-powered tabletop RPG application. The application facilitates character creation, campaign management, and gameplay through an interactive chat-based interface combined with traditional game UI elements.

---

## Pages

### 1. Startup Menu
**Purpose**: Entry point for the application where users select or create campaigns and characters.

**Requirements**:
- Display list of available campaigns
- Allow campaign selection
- Display list of user's characters for selected campaign
- Allow character selection
- Provide options to create new campaign or character
- Handle loading states gracefully
- Navigate to appropriate creation or game pages based on selection

---

### 2. Campaign Creation Page
**Purpose**: Enable users to create new game campaigns with custom or premade worlds.

**Requirements**:
- Form-based campaign setup interface
- Option to select premade world templates
- Fields for custom campaign details:
  - Campaign name
  - Description
  - Player acceptance settings
- Split-screen layout:
  - Left side: World details display
  - Right side: AI assistant chat for campaign guidance
- Integration with world-building chat agent
- Display world state information as it's being created

---

### 3. Character Creation Page
**Purpose**: Guide users through creating their RPG character with AI assistance.

**Requirements**:
- Split-screen layout:
  - Left side: Live character sheet preview
  - Right side: AI chat assistant for character creation
- Interactive character sheet that updates in real-time
- Chat-driven creation process
- AI agent introduces process and guides through steps
- Save character upon completion

---

### 4. Character Lobby Page
**Purpose**: Pre-game waiting room where players ready up before starting.

**Requirements**:
- Display campaign name and description
- Show grid of all characters in the party
- Visual indication of ready/waiting status for each character
- Character portraits/images
- Toggle ready button for current player
- Auto-start detection when all players are ready
- Visual feedback for game start

---

### 5. Game Page
**Purpose**: Main gameplay interface combining narrative, character management, and AI interaction.

**Requirements**:
- Split-screen layout:
  - Left side: Game information tabs
  - Right side: AI game master chat
- Tabbed interface for:
  - **Story Tab**: Narrative history and updates
  - **Characters Tab**: Active character sheet with full details
  - **World Tab**: World information including locations, NPCs, quests, events
  - **Combat Tab**: Combat encounter details (visible only during combat)
- Party management button to view all party members
- Chat-driven gameplay with AI game master
- Real-time state synchronization across all components

---

## Core Components

### Chat Components

#### Chat View
**Purpose**: Display conversation history between user and AI agents.

**Requirements**:
- Scrollable message container
- Display messages chronologically
- Support for timestamps (optional)
- Auto-scroll to new messages
- Manual scroll button when user scrolls up
- Message removal capability
- Support for message editing/updating

#### Message View
**Purpose**: Render individual messages in the chat.

**Requirements**:
- Display formatted message content (markdown support using Markdig)
- Show embedded images from URLs
- Edit and delete functionality
- Different styling for user vs. AI messages
- User messages start with specific player identifier
- Support for rich text formatting
- Image grid/gallery for multiple images
- Inline editing mode

#### User Input
**Purpose**: Capture and submit user messages to the AI agent.

**Requirements**:
- Text input field (single line or multi-line)
- Toggle between text box and text area
- Submit button with loading state
- Cancel button to stop AI processing



#### Start Overlay
**Purpose**: Initial screen overlay before beginning gameplay or creation.

**Requirements**:
- Welcome message or instructions
- Start button to begin
- Visual styling consistent with RPG theme
- Dismissible overlay

---

### Game Components

#### Character Sheet View
**Purpose**: Display and edit complete character information.

**Requirements**:
- Multi-page layout for organizing extensive character data
- **Page 1 - Core Stats**:
  - Basic info: name, player name, race, class, level, alignment, deity
  - Attribute scores with calculated modifiers (Might, Agility, Vitality, Wits, Presence)
  - Combat stats: HP, MP, AC, initiative, speed
  - Weapons table with attack bonuses and damage
  - Skills list with ranks and bonuses
  - Spellcasting section (if applicable):
    - Tradition and ability
    - Spell save DC and attack modifier
    - Spell slots by level
    - Known spells organized by band (tier)
- **Page 2 - Abilities & Equipment**:
  - Class abilities with descriptions
  - Racial traits with descriptions
  - Currency (gold, silver, copper)
  - Inventory table
  - Armor and shield information
  - Background details: personality traits, ideals, bonds, flaws, notes
- **Page 3 - Character Portrait**:
  - Character image/artwork
- Form validation and submission
- Read-only fields for calculated values
- Editable fields for direct character modification

#### Narrative View
**Purpose**: Display story progression and narrative updates.

**Requirements**:
- Tabbed interface:
  - Global narrative (all players see)
  - GM narrative (game master notes)
  - Character-specific narratives
- Chronological history display
- Rich text formatting support (markdown to HTML)
- Visual separators between narrative entries
- Scrollable content area

#### Combat Encounter Details
**Purpose**: Display combat state, combatants, and action history.

**Requirements**:
- Collapsible sections:
  - **Initiative Order**:
    - Display turn order with initiative values
    - Highlight current active combatant
    - Show which combatants have taken their turn
    - Distinguish player characters from monsters
  - **Combatants**:
    - **Party Members**:
      - Name and basic info
      - Health bars with current/max HP
      - Temporary HP display
      - Unconscious status with death saves
    - **Monsters**:
      - Name, type, and challenge rating
      - Health bars
      - Special attacks/abilities
      - Clickable for detailed view
  - **Combat Log**:
    - Chronological action history
    - Attack rolls, damage dealt, spell casting, etc.
- Modal/overlay for detailed combatant information
- Combat status (active, resolved) and round counter
- Environment description
- Visual styling for different combat states

#### World Details
**Purpose**: Display comprehensive world information.

**Requirements**:
- Collapsible sections:
  - **World Description**:
    - Name and brief summary
    - Full detailed description
  - **Locations**:
    - Grid/list of all locations
    - Clickable cards for expanded details
    - Location tags
    - Selection highlighting
  - **NPCs**:
    - Character cards with name and status
    - Disposition indicators (friendly, neutral, hostile)
    - Current location information
    - Expandable descriptions
    - Visual status indicators (alive, dead, unknown)
  - **Quests**:
    - Quest cards with name and location
    - Detailed descriptions
    - Quest giver information
    - Reward details (gold, items)
    - Selection/expansion functionality
  - **Events**:
    - Event cards with name and status
    - Active vs. resolved indicators
    - Related locations and entities
    - Start time and timing information
    - Event tags and notes
- Loading states
- Empty states when no data available
- Smooth expand/collapse animations

#### Party View
**Purpose**: Display all party members in a compact format.

**Requirements**:
- List of party members with cards
- Each member shows:
  - Player ID
  - Character name and level
  - Race and class
  - Current/max HP
  - Attribute scores (condensed)
- Scrollable container
- Close/dismiss button
- Typically displayed as modal/dialog

---

## UI/UX Requirements

### General Design Principles
- **RPG Theme**: Fantasy/medieval aesthetic with parchment, medieval fonts, and thematic colors
- **Responsive Layout**: Support for various screen sizes
- **Accessibility**: Proper labels, contrast ratios, keyboard navigation
- **Loading States**: Clear feedback during async operations
- **Error Handling**: User-friendly error messages

### Layout Patterns
- **Split-screen**: Common pattern with game info on left, AI chat on right
- **Tabs**: Organize related information without cluttering interface
- **Cards**: Contain related information in visually distinct blocks
- **Collapsible Sections**: Hide/show detailed information on demand
- **Modals/Overlays**: Display detailed information without navigation

### Interaction Patterns
- **Click to expand**: Many elements show more detail on selection
- **Real-time updates**: UI reflects state changes immediately
- **Form validation**: Inline validation with clear error messages
- **Confirmation dialogs**: For destructive actions
- **Disabled states**: Visual indication when actions unavailable

### Data Display
- **Health bars**: Visual representation of HP/resource levels
- **Status indicators**: Color-coded badges for various states
- **Progress indicators**: Show loading or processing states
- **Tables**: Structured data for stats, inventory, skills
- **Lists**: Sequential items like combat logs, narrative history
- **Grids**: Multiple items of same type (characters, locations, quests)

### Chat Interaction
- **Markdown rendering**: Support rich text in messages
- **Image display**: Inline images in chat
- **Streaming responses**: Show AI responses as they generate
- **Edit history**: Allow correction of user inputs
- **Context preservation**: Maintain conversation history

---

## State Management Requirements

### Application State
- Current user/player information
- Active campaign and character
- Global game state (world, narrative, combat)
- Chat states for each agent type
- UI state (selected tabs, expanded sections, modals)

### Synchronization
- Real-time updates across all components
- Cascading state from parent to child components
- Event-driven updates for state changes
- Optimistic UI updates with rollback capability

---

## Functional Requirements

### Navigation Flow
1. Startup → Select/Create Campaign → Select/Create Character → Lobby → Game
2. Support for returning to previous steps
3. Deep linking to specific pages/states

### Agent Integration
- Multiple AI agent types for different purposes:
  - Character creation agent
  - World building agent
  - Game master agent
- Context-aware agent instructions
- Direct agent communication vs. orchestrated multi-agent
- Chat history persistence

### Data Persistence
- Save character sheets
- Save campaign state
- Save world information
- Save narrative history
- Export/import capability

### Real-time Collaboration
- Multiple players in same campaign
- Synchronized game state
- Ready-up system for coordinated start
- Turn-based combat coordination

---

## Technical Considerations

### Performance
- Efficient rendering of long chat histories
- Virtual scrolling for large lists
- Lazy loading of detailed information
- Optimized re-rendering on state changes

### Browser Compatibility
- Modern browser support
- Graceful degradation for older browsers
- Mobile browser considerations

### Accessibility
- Screen reader support
- Keyboard navigation
- ARIA labels and roles
- Focus management
- Color contrast compliance

---

## Future Enhancement Opportunities

### UI Improvements
- Drag-and-drop character sheet elements
- Visual dice rolling animations
- Interactive combat map/grid
- Character portrait generation
- Audio narration of story elements
- Dark/light theme toggle

### Feature Additions
- Character comparison view
- Quest tracking sidebar
- Inventory management improvements
- Spell book interface
- Combat damage calculator
- Session recording/replay
- Voice input for chat

### Social Features
- Party chat separate from GM chat
- Player-to-player private messaging
- Session scheduling
- Campaign sharing/discovery
- Achievement system

---

## Technical Requirements
- Use code-behind model for UI logic - .razor.cs files as partial classes.
- Use scoped CSS for component-specific styling - .razor.css files.

## Conclusion

This UI architecture supports a rich, interactive tabletop RPG experience powered by AI agents. The split-screen pattern with chat on one side and game information on the other provides a natural flow for AI-assisted gameplay while maintaining access to all necessary game data. The component-based structure allows for flexibility and reusability across different pages and contexts.